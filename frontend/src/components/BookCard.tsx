import type { BookCandidate, MatchTier } from '../types/api';

interface Props {
  candidate: BookCandidate;
}

const tierStyles: Record<MatchTier, string> = {
  Exact: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/50 dark:text-emerald-200',
  Strong: 'bg-sky-100 text-sky-800 dark:bg-sky-900/50 dark:text-sky-200',
  Good: 'bg-indigo-100 text-indigo-800 dark:bg-indigo-900/50 dark:text-indigo-200',
  Weak: 'bg-slate-200 text-slate-700 dark:bg-slate-800 dark:text-slate-300',
  None: 'bg-slate-200 text-slate-500 dark:bg-slate-800 dark:text-slate-500',
};

export function BookCard({ candidate }: Props) {
  const { book, tier, explanation, ruleName } = candidate;

  return (
    <article className="flex gap-4 rounded-xl border border-slate-200 bg-white p-4 shadow-sm transition hover:shadow-md dark:border-slate-800 dark:bg-slate-900">
      <div className="h-32 w-24 flex-shrink-0 overflow-hidden rounded-md bg-slate-100 dark:bg-slate-800">
        {book.coverUrl ? (
          <img
            src={book.coverUrl}
            alt={`Cover of ${book.title}`}
            className="h-full w-full object-cover"
            loading="lazy"
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center text-xs text-slate-400">no cover</div>
        )}
      </div>

      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex items-start justify-between gap-3">
          <a
            href={book.openLibraryUrl}
            target="_blank"
            rel="noreferrer noopener"
            className="truncate text-base font-semibold text-slate-900 hover:underline dark:text-slate-100"
            title={book.title}
          >
            {book.title}
          </a>
          <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tierStyles[tier]}`}>{tier}</span>
        </div>

        {book.primaryAuthors.length > 0 && (
          <p className="text-sm text-slate-600 dark:text-slate-400">
            <span className="font-medium">Primary:</span> {book.primaryAuthors.join(', ')}
          </p>
        )}
        {book.contributors.length > 0 && (
          <p className="text-xs text-slate-500 dark:text-slate-500">
            Contributors: {book.contributors.slice(0, 3).join(', ')}
            {book.contributors.length > 3 ? '…' : ''}
          </p>
        )}
        {book.firstPublishYear !== null && (
          <p className="text-xs text-slate-500 dark:text-slate-500">First published {book.firstPublishYear}</p>
        )}

        <p className="mt-1 text-sm text-slate-700 dark:text-slate-300">{explanation}</p>
        <p className="text-[11px] uppercase tracking-wide text-slate-400 dark:text-slate-600">rule: {ruleName}</p>
      </div>
    </article>
  );
}
