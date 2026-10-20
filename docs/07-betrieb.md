# 07 – Betrieb, CI und Sicherheit

Stand: P3. Deployment (Caddy, GHCR, `docker-compose.prod.yml`) folgt in einem späteren Prompt.

## CI (GitHub Actions)

| Workflow | Inhalt |
|---|---|
| `build-and-test.yml` | Backend bauen und testen (inkl. Testcontainers, Docker-Daemon des Runners), Coverage als Artefakt; Frontend `npm ci`, Lint, Test mit Coverage, Build; gitleaks über die gesamte Historie |
| `codeql.yml` | CodeQL für C# und TypeScript bei Push, PR und wöchentlich |
| `dependabot.yml` | wöchentliche Updates für NuGet, npm, Docker und GitHub Actions |

- **Mindestabdeckung TaxEngine 95 %:** `scripts/check-coverage.py` liest die Cobertura-Berichte. Solange die
  TaxEngine noch keinen Code hat, wird die Prüfung übersprungen.
- Lokal: `dotnet test src/backend/KryptoSteuer.sln`, im Frontend `npm run lint`, `npm test`, `npm run build`
  (Node-Version laut `src/frontend/.nvmrc`). Mit npm 10 bricht `npm install` wegen eines npm-Bugs ab
  (`Cannot read properties of null (reading 'edgesOut')`); Node 24 bringt npm 11 mit, dort läuft es.

## Branch-Schutz (Empfehlung für `main`)

Unter Settings → Branches → Branch protection rule:

- Pull Request vor dem Merge verlangen (mindestens die eigene Prüfung des Diffs).
- Status Checks verlangen und aktuell halten: `backend`, `frontend`, `secrets`, `analyze (csharp)`,
  `analyze (javascript-typescript)`.
- Direkte Pushes und Force-Pushes auf `main` verbieten.
- Verlauf linear halten (Squash-Merge).

## Geheimnisse

| Wo | Wie |
|---|---|
| Lokale Entwicklung | .NET User Secrets: `dotnet user-secrets set "Key" "Wert" --project src/backend/Api` |
| Container | Umgebungsvariablen aus `.env` (nie eingecheckt; `.env.example` enthält nur Dev-Defaults) |
| Börsen-API-Keys | in der Datenbank nur verschlüsselt, über `ISecretProtector` (ASP.NET Data Protection) |
| Verhinderung von Leaks | gitleaks als Pre-Commit-Hook und als CI-Schritt |

Bitget-Keys nur mit Leserechten anlegen.

### Schlüsselring der Verschlüsselung

Data Protection verschlüsselt mit Schlüsseln, die **nicht** in der Datenbank liegen:

- Im Container: Verzeichnis `/keys` (`DataProtection__KeysPath`), Docker-Volume `dpkeys`.
- Lokal ohne Konfiguration: Standardort des Betriebssystems im Benutzerprofil.
- **Backup:** Datenbank und Schlüsselring getrennt sichern. Ohne den Schlüsselring sind gespeicherte
  Börsen-Keys nicht mehr entschlüsselbar (dann neu eintragen); zusammen mit dem Schlüsselring sind sie lesbar.
  Backups des Schlüsselrings entsprechend schützen.
- Die Schlüssel rotieren automatisch (Standard 90 Tage); alte Schlüssel bleiben zum Entschlüsseln erhalten.

### Pre-Commit-Hook aktivieren

```bash
# gitleaks installieren: https://github.com/gitleaks/gitleaks#installing
git config core.hooksPath .githooks
```

Der Hook bricht den Commit ab, wenn gitleaks fehlt oder ein Geheimnis findet.

## Pull Requests

`.github/pull_request_template.md` enthält die Checkliste: Tests, Doku, CHANGELOG, Steuerregel betroffen ja/nein.
