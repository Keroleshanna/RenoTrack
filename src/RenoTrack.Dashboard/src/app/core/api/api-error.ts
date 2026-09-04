import { HttpErrorResponse } from '@angular/common/http';

/**
 * The API's RFC 7807 `ProblemDetails`, turned into something a user can read.
 *
 * **The server's `detail` is never rendered where a user-facing message belongs** (CLAUDE.md §23).
 * Every mapped exception in the backend is phrased for an API caller, in English — *"Cannot edit
 * Angebot 8002: status is 'InReview'…"* is correct for a developer and useless to the office. So the
 * Dashboard maps the **status code** onto its own dictionary string. `detail` is kept on the object
 * for logging, never for display.
 */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly traceId?: string;
  /** Present only on a 400 from FluentValidation — keyed by field name. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

/**
 * What the Dashboard does about a failure, chosen by status code.
 *
 * Deliberately coarse: a screen needs to know *was this my input, my permissions, or the server*.
 * The precise Application exception behind it changes nothing about what the user is told.
 */
export type ApiErrorKind =
  | 'validation'
  | 'unauthenticated'
  | 'forbidden'
  | 'notFound'
  | 'conflict'
  | 'gone'
  | 'offline'
  | 'server';

export class ApiError extends Error {
  constructor(
    readonly kind: ApiErrorKind,
    readonly status: number,
    readonly problem: ProblemDetails | null,
    /** Field name → first message, for a 400. Empty for every other kind. */
    readonly fieldErrors: Readonly<Record<string, string>>,
  ) {
    super(problem?.detail ?? problem?.title ?? `HTTP ${status}`);
    this.name = 'ApiError';
  }

  /** The `traceId` to quote when reporting a fault. */
  get traceId(): string | null {
    return this.problem?.traceId ?? null;
  }
}

export function toApiError(response: HttpErrorResponse): ApiError {
  // Status 0 means the request never reached the server: offline, DNS, or a dev proxy with no API
  // behind it. Worth distinguishing, because "try again" is the right advice and "your input was
  // wrong" is not.
  if (response.status === 0) {
    return new ApiError('offline', 0, null, {});
  }

  const problem = isProblemDetails(response.error) ? response.error : null;

  return new ApiError(kindFor(response.status), response.status, problem, fieldErrorsOf(problem));
}

function kindFor(status: number): ApiErrorKind {
  switch (status) {
    case 400:
      return 'validation';
    case 401:
      return 'unauthenticated';
    case 403:
      return 'forbidden';
    case 404:
      return 'notFound';
    case 409:
      return 'conflict';
    case 410:
      return 'gone';
    default:
      return 'server';
  }
}

/**
 * Flattens `errors` to one message per field — a form shows one error under one control.
 *
 * **Field names are lower-cased.** ASP.NET keys them by the C# property (`Comment`), while Angular
 * form controls are camelCase, so a caller can look up `comment` without knowing which side named it.
 */
function fieldErrorsOf(problem: ProblemDetails | null): Readonly<Record<string, string>> {
  if (!problem?.errors) {
    return {};
  }

  const flattened: Record<string, string> = {};
  for (const [field, messages] of Object.entries(problem.errors)) {
    if (messages?.length) {
      flattened[field.charAt(0).toLowerCase() + field.slice(1)] = messages[0];
    }
  }
  return flattened;
}

function isProblemDetails(body: unknown): body is ProblemDetails {
  return typeof body === 'object' && body !== null && !Array.isArray(body);
}
