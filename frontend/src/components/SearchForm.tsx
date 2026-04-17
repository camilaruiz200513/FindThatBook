import { useState, type FormEvent } from 'react';

interface Props {
  disabled: boolean;
  onSubmit: (query: string, maxResults: number) => void;
}

export function SearchForm({ disabled, onSubmit }: Props) {
  const [query, setQuery] = useState('');
  const [maxResults, setMaxResults] = useState(5);

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    const trimmed = query.trim();
    if (trimmed.length === 0 || disabled) return;
    onSubmit(trimmed, maxResults);
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label htmlFor="query" className="mb-2 block text-sm font-medium text-slate-700 dark:text-slate-300">
          Describe the book you're looking for
        </label>
        <input
          id="query"
          type="text"
          placeholder="e.g. tolkien hobbit illustrated 1937"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          disabled={disabled}
          className="w-full rounded-lg border border-slate-300 bg-white px-4 py-3 text-base shadow-sm transition focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/40 disabled:opacity-60 dark:border-slate-700 dark:bg-slate-900"
          aria-label="Book search query"
        />
      </div>

      <div className="flex flex-wrap items-center gap-4">
        <label htmlFor="max" className="text-sm text-slate-600 dark:text-slate-400">
          Max results
          <select
            id="max"
            value={maxResults}
            onChange={(e) => setMaxResults(Number(e.target.value))}
            disabled={disabled}
            className="ml-2 rounded-md border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-900"
          >
            {[3, 5, 10, 15, 20].map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>

        <button
          type="submit"
          disabled={disabled || query.trim().length === 0}
          className="ml-auto rounded-lg bg-brand-600 px-5 py-2.5 text-sm font-medium text-white shadow transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {disabled ? 'Searching…' : 'Find book'}
        </button>
      </div>
    </form>
  );
}
