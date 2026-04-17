import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { FindBookResponse } from './types/api';

const successResponse: FindBookResponse = {
  originalQuery: 'tolkien hobbit',
  hypothesis: {
    title: 'The Hobbit',
    author: 'J.R.R. Tolkien',
    year: 1937,
    keywords: [],
  },
  candidates: [
    {
      book: {
        workId: '/works/OL262758W',
        title: 'The Hobbit',
        primaryAuthors: ['J.R.R. Tolkien'],
        contributors: [],
        firstPublishYear: 1937,
        coverId: null,
        subjects: [],
        isbns: [],
        openLibraryUrl: 'https://openlibrary.org/works/OL262758W',
        coverUrl: null,
        allAuthors: ['J.R.R. Tolkien'],
      },
      tier: 'Exact',
      ruleName: 'ExactTitlePrimaryAuthorRule',
      explanation: 'Exact title match and primary author match.',
    },
  ],
  totalCandidates: 1,
  processingTime: '00:00:00.5000000',
};

describe('App', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the idle empty state on first load', () => {
    render(<App />);
    expect(screen.getByRole('heading', { name: /Find That Book/i })).toBeInTheDocument();
    expect(screen.getByText(/Try a noisy query/i)).toBeInTheDocument();
  });

  it('shows candidates after a successful search', async () => {
    (fetch as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve(successResponse),
    });

    const user = userEvent.setup();
    render(<App />);
    await user.type(screen.getByLabelText(/book search query/i), 'tolkien hobbit');
    await user.click(screen.getByRole('button', { name: /find book/i }));

    await waitFor(() =>
      expect(screen.getAllByText('The Hobbit').length).toBeGreaterThan(0),
    );
    expect(screen.getByText(/LLM hypothesis/i)).toBeInTheDocument();
    expect(screen.getByText('Exact')).toBeInTheDocument();
    // The book link in the candidate card points at the OL work page.
    expect(screen.getByRole('link', { name: /The Hobbit/ })).toHaveAttribute(
      'href',
      'https://openlibrary.org/works/OL262758W',
    );
  });

  it('shows an error banner with correlation id when the API fails', async () => {
    (fetch as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: false,
      status: 500,
      json: () =>
        Promise.resolve({
          title: 'Internal Server Error',
          detail: 'Something broke',
          correlationId: 'abc-123',
        }),
    });

    const user = userEvent.setup();
    render(<App />);
    await user.type(screen.getByLabelText(/book search query/i), 'anything');
    await user.click(screen.getByRole('button', { name: /find book/i }));

    await waitFor(() =>
      expect(screen.getByText('Something broke')).toBeInTheDocument(),
    );
    expect(screen.getByText(/abc-123/)).toBeInTheDocument();
  });
});
