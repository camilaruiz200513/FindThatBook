import type { FindBookRequest, FindBookResponse, ProblemDetails } from '../types/api';
import { ApiError } from '../types/api';

const DEFAULT_BASE_URL = ''; // Relative — handled by Vite dev proxy or same-origin deploy.

export async function findBooks(
  request: FindBookRequest,
  signal?: AbortSignal,
): Promise<FindBookResponse> {
  const base = (import.meta.env.VITE_API_URL as string | undefined) ?? DEFAULT_BASE_URL;
  const response = await fetch(`${base}/api/books/find`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
    signal,
  });

  if (response.ok) {
    return (await response.json()) as FindBookResponse;
  }

  let problem: ProblemDetails | undefined;
  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    /* non-JSON body */
  }

  throw new ApiError(
    problem?.detail ?? problem?.title ?? `Request failed with status ${response.status}`,
    response.status,
    problem,
  );
}
