import { useRef, useState } from 'react';
import { EmptyState } from './components/EmptyState';
import { ErrorBanner } from './components/ErrorBanner';
import { HypothesisPanel } from './components/HypothesisPanel';
import { LoadingSpinner } from './components/LoadingSpinner';
import { ResultsList } from './components/ResultsList';
import { SearchForm } from './components/SearchForm';
import { findBooks } from './services/api';
import { ApiError, type FindBookResponse } from './types/api';

type ViewState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'error'; message: string; correlationId?: string }
  | { kind: 'success'; response: FindBookResponse };

export default function App() {
  const [state, setState] = useState<ViewState>({ kind: 'idle' });
  const abortRef = useRef<AbortController | null>(null);

  const handleSubmit = async (query: string, maxResults: number) => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setState({ kind: 'loading' });
    try {
      const response = await findBooks({ query, maxResults }, controller.signal);
      setState({ kind: 'success', response });
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') return;
      if (err instanceof ApiError) {
        setState({
          kind: 'error',
          message: err.message,
          correlationId: err.problem?.correlationId,
        });
      } else {
        setState({
          kind: 'error',
          message: err instanceof Error ? err.message : 'Unknown error',
        });
      }
    }
  };

  return (
    <div className="mx-auto max-w-3xl px-4 py-10">
      <header className="mb-10">
        <h1 className="text-3xl font-bold tracking-tight">Find That Book</h1>
        <p className="mt-2 text-slate-600 dark:text-slate-400">
          Describe a book in your own words. We use an LLM to reconstruct your intent and search Open Library for the
          best matches, explaining why each one was chosen.
        </p>
      </header>

      <section className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900">
        <SearchForm disabled={state.kind === 'loading'} onSubmit={handleSubmit} />
      </section>

      <section className="mt-6 space-y-4">
        {state.kind === 'loading' && (
          <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900">
            <LoadingSpinner label="Asking Gemini and Open Library" />
          </div>
        )}

        {state.kind === 'error' && (
          <ErrorBanner
            message={state.message}
            correlationId={state.correlationId}
            onDismiss={() => setState({ kind: 'idle' })}
          />
        )}

        {state.kind === 'success' && (
          <>
            <HypothesisPanel hypothesis={state.response.hypothesis} />
            {state.response.candidates.length === 0 ? (
              <EmptyState
                title="No confident matches"
                description="We couldn't find a book that matches the hypothesis closely enough. Try adding an author or year."
              />
            ) : (
              <ResultsList candidates={state.response.candidates} />
            )}
            <p className="text-right text-xs text-slate-400">
              {state.response.totalCandidates} match{state.response.totalCandidates === 1 ? '' : 'es'} · processed in{' '}
              {state.response.processingTime}
            </p>
          </>
        )}

        {state.kind === 'idle' && (
          <EmptyState
            title="Try a noisy query"
            description='Examples: "tolkien hobbit illustrated 1937", "mark huckleberry", "orwell 1984".'
          />
        )}
      </section>

      <footer className="mt-10 text-center text-xs text-slate-400">
        Powered by Gemini · Open Library · FindThatBook API
      </footer>
    </div>
  );
}
