export function LoadingSpinner({ label = 'Loading' }: { label?: string }) {
  return (
    <div role="status" aria-live="polite" className="flex items-center gap-3 text-slate-600 dark:text-slate-400">
      <span
        className="inline-block h-5 w-5 animate-spin rounded-full border-2 border-slate-300 border-t-brand-500 dark:border-slate-700 dark:border-t-brand-500"
        aria-hidden
      />
      <span className="text-sm">{label}…</span>
    </div>
  );
}
