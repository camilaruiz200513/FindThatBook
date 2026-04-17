import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { BookCard } from './BookCard';
import type { BookCandidate } from '../types/api';

const sampleCandidate: BookCandidate = {
  book: {
    workId: '/works/OL262758W',
    title: 'The Hobbit',
    primaryAuthors: ['J.R.R. Tolkien'],
    contributors: ['Alan Lee', 'John Howe'],
    firstPublishYear: 1937,
    coverId: '12345',
    subjects: ['Fantasy'],
    isbns: ['0-618-00221-9'],
    openLibraryUrl: 'https://openlibrary.org/works/OL262758W',
    coverUrl: 'https://covers.openlibrary.org/b/id/12345-M.jpg',
    allAuthors: ['J.R.R. Tolkien', 'Alan Lee', 'John Howe'],
  },
  tier: 'Exact',
  ruleName: 'ExactTitlePrimaryAuthorRule',
  explanation: "Exact title match and primary author match ('J.R.R. Tolkien'); year 1937 matches.",
};

describe('BookCard', () => {
  it('renders title, tier badge, primary authors, and explanation', () => {
    render(<BookCard candidate={sampleCandidate} />);

    expect(screen.getByRole('link', { name: /The Hobbit/ })).toBeInTheDocument();
    expect(screen.getByText('Exact')).toBeInTheDocument();
    expect(screen.getAllByText(/J.R.R. Tolkien/).length).toBeGreaterThan(0);
    expect(screen.getByText(/primary author match/i)).toBeInTheDocument();
    expect(screen.getByText(/rule:/i).parentElement!.textContent).toContain('ExactTitlePrimaryAuthorRule');
  });

  it('links to the Open Library work page', () => {
    render(<BookCard candidate={sampleCandidate} />);
    const link = screen.getByRole('link', { name: /The Hobbit/ });
    expect(link).toHaveAttribute('href', 'https://openlibrary.org/works/OL262758W');
    expect(link).toHaveAttribute('rel', 'noreferrer noopener');
  });

  it('falls back to a placeholder when there is no cover', () => {
    const noCover = {
      ...sampleCandidate,
      book: { ...sampleCandidate.book, coverUrl: null, coverId: null },
    };
    render(<BookCard candidate={noCover} />);
    expect(screen.getByText(/no cover/i)).toBeInTheDocument();
  });
});
