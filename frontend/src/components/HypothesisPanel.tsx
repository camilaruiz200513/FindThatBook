import type { ExtractedBookInfo } from '../types/api';

interface Props {
  hypothesis: ExtractedBookInfo;
}

export function HypothesisPanel({ hypothesis }: Props) {
  const hasAny =
    hypothesis.title !== null ||
    hypothesis.author !== null ||
    hypothesis.year !== null ||
    hypothesis.keywords.length > 0;

  if (!hasAny) {
    return (
      <div className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm text-amber-800 dark:border-amber-700 dark:bg-amber-950/40 dark:text-amber-200">
        The LLM could not derive a clear hypothesis from your query.
      </div>
    );
  }

  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-800 dark:bg-slate-900">
      <h3 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
        LLM hypothesis
      </h3>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-4">
        <Cell label="Title" value={hypothesis.title} />
        <Cell label="Author" value={hypothesis.author} />
        <Cell label="Year" value={hypothesis.year?.toString() ?? null} />
        <Cell label="Keywords" value={hypothesis.keywords.length > 0 ? hypothesis.keywords.join(', ') : null} />
      </dl>
    </div>
  );
}

function Cell({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <dt className="text-xs font-medium text-slate-500 dark:text-slate-400">{label}</dt>
      <dd className="mt-0.5 text-slate-900 dark:text-slate-100">
        {value ?? <span className="italic text-slate-400">not extracted</span>}
      </dd>
    </div>
  );
}
