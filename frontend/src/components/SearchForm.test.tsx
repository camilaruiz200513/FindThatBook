import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { SearchForm } from './SearchForm';

describe('SearchForm', () => {
  it('submits the trimmed query and chosen maxResults', async () => {
    const onSubmit = vi.fn();
    const user = userEvent.setup();
    render(<SearchForm disabled={false} onSubmit={onSubmit} />);

    const input = screen.getByLabelText(/book search query/i);
    await user.type(input, '  tolkien hobbit 1937  ');
    await user.click(screen.getByRole('button', { name: /find book/i }));

    expect(onSubmit).toHaveBeenCalledWith('tolkien hobbit 1937', 5);
  });

  it('does not submit empty queries', async () => {
    const onSubmit = vi.fn();
    const user = userEvent.setup();
    render(<SearchForm disabled={false} onSubmit={onSubmit} />);

    await user.click(screen.getByRole('button', { name: /find book/i }));

    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('disables inputs and shows progress label when disabled', () => {
    render(<SearchForm disabled={true} onSubmit={() => {}} />);

    expect(screen.getByLabelText(/book search query/i)).toBeDisabled();
    expect(screen.getByRole('button', { name: /searching/i })).toBeDisabled();
  });
});
