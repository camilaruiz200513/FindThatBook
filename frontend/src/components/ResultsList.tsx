import type { BookCandidate } from '../types/api';
import { BookCard } from './BookCard';

interface Props {
  candidates: BookCandidate[];
}

export function ResultsList({ candidates }: Props) {
  return (
    <ul className="space-y-3" aria-label="Book candidates">
      {candidates.map((candidate) => (
        <li key={candidate.book.workId}>
          <BookCard candidate={candidate} />
        </li>
      ))}
    </ul>
  );
}
