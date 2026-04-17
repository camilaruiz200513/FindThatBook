export type MatchTier = 'None' | 'Weak' | 'Good' | 'Strong' | 'Exact';

export interface ExtractedBookInfo {
  title: string | null;
  author: string | null;
  year: number | null;
  keywords: string[];
}

export interface Book {
  workId: string;
  title: string;
  primaryAuthors: string[];
  contributors: string[];
  firstPublishYear: number | null;
  coverId: string | null;
  subjects: string[];
  isbns: string[];
  openLibraryUrl: string;
  coverUrl: string | null;
  allAuthors: string[];
}

export interface BookCandidate {
  book: Book;
  tier: MatchTier;
  ruleName: string;
  explanation: string;
}

export interface FindBookRequest {
  query: string;
  maxResults?: number;
}

export interface FindBookResponse {
  originalQuery: string;
  hypothesis: ExtractedBookInfo;
  candidates: BookCandidate[];
  totalCandidates: number;
  processingTime: string;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Array<{ propertyName: string; errorMessage: string }>;
  correlationId?: string;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ProblemDetails;

  constructor(message: string, status: number, problem?: ProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}
