# Build-Kontext: src/frontend
FROM node:26-alpine AS build
WORKDIR /app
COPY package*.json ./
# Mit Lockfile reproduzierbar (npm ci); ohne Lockfile (erster Start) npm install.
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi
COPY . .
RUN npm run build

FROM nginx:1.29-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/krypto-steuer/browser /usr/share/nginx/html
EXPOSE 80
