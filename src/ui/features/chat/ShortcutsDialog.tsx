import { KeyboardIcon } from 'lucide-react';
import { Button } from '../../components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '../../components/ui/dialog';

const SHORTCUTS: Array<[string, string]> = [
  ['New thread', '⌘ N'],
  ['Toggle sidebar', '⌘ B'],
  ['Switch Chat / Code', '⌘ ⇧ M'],
  ['Focus prompt', '⌘ L'],
  ['Open Settings', '⌘ ,'],
  ['Retry last message', '⌘ ⇧ R'],
  ['Stop run / close modal', 'Esc'],
  ['Show shortcuts', '⌘ /'],
];

export function ShortcutsDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <KeyboardIcon size={16} />
            <span>Keyboard shortcuts</span>
          </DialogTitle>
          <DialogDescription>
            Use Cmd on macOS or Ctrl on Windows and Linux.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-2 py-2 text-xs">
          {SHORTCUTS.map(([label, keys]) => (
            <div
              key={label}
              className="flex items-center justify-between rounded-lg border border-border/60 bg-[var(--bg-panel-subtle)] px-3 py-2"
            >
              <span className="text-foreground">{label}</span>
              <kbd className="rounded border border-border bg-[var(--bg-panel)] px-2 py-0.5 font-mono text-[11px] text-muted-foreground">
                {keys}
              </kbd>
            </div>
          ))}
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="outline" size="sm">
              Close
            </Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
