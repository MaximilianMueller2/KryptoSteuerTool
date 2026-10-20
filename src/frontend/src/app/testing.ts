import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { ComponentFixture } from '@angular/core/testing';

export function setup<T>(component: Type<T>): { http: HttpTestingController; create: () => ComponentFixture<T> } {
  TestBed.configureTestingModule({ imports: [component], providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { http: TestBed.inject(HttpTestingController), create: () => TestBed.createComponent(component) };
}

export function el(fixture: ComponentFixture<unknown>, testId: string): HTMLElement | null {
  return (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);
}

export function text(fixture: ComponentFixture<unknown>, testId: string): string {
  return el(fixture, testId)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}
