interface Props {
  message: string;
  correlationId?: string;
  onDismiss?: () => void;
}

export function ErrorBanner({ message, correlationId, onDismiss }: Props) {
  return (
    <div
      role="alert"
      className="flex items-start gap-3 rounded-lg border border-rose-300 bg-rose-50 p-4 text-sm text-rose-800 dark:border-rose-700 dark:bg-rose-950/40 dark:text-rose-200"
    >
      <div className="flex-1">
        <p className="font-medium">Something went wrong</p>
        <p className="mt-1">{message}</p>
        {correlationId && (
          <p className="mt-2 text-xs opacity-70">
            Correlation ID: <code className="font-mono">{correlationId}</code>
          </p>
        )}
      </div>
      {onDismiss && (
        <button
          type="button"
          onClick={onDismiss}
          className="rounded-md px-2 py-1 text-xs hover:bg-rose-100 dark:hover:bg-rose-900/40"
          aria-label="Dismiss error"
        >
          ✕
        </button>
      )}
    </div>
  );
}
