import * as React from "react"
import { Dialog as DialogPrimitive } from "radix-ui"
import { XIcon } from "lucide-react"
import { cn } from "@/lib/utils"

const DialogDismissContext = React.createContext<(() => void) | undefined>(undefined)

function Dialog({
  onOpenChange,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Root>) {
  const dismiss = React.useCallback(() => {
    onOpenChange?.(false)
  }, [onOpenChange])

  return (
    <DialogDismissContext.Provider value={dismiss}>
      <DialogPrimitive.Root data-slot="dialog" onOpenChange={onOpenChange} {...props} />
    </DialogDismissContext.Provider>
  )
}

function DialogTrigger({
  render,
  children,
  asChild,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Trigger> & {
  render?: React.ReactElement
}) {
  if (render) {
    return (
      <DialogPrimitive.Trigger data-slot="dialog-trigger" asChild {...props}>
        {render}
      </DialogPrimitive.Trigger>
    )
  }
  return (
    <DialogPrimitive.Trigger
      data-slot="dialog-trigger"
      asChild={asChild}
      {...props}
    >
      {children}
    </DialogPrimitive.Trigger>
  )
}

function DialogPortal({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Portal>) {
  return <DialogPrimitive.Portal data-slot="dialog-portal" {...props} />
}

function DialogClose({
  render,
  children,
  asChild,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Close> & {
  render?: React.ReactElement
}) {
  if (render) {
    return (
      <DialogPrimitive.Close data-slot="dialog-close" asChild {...props}>
        {render}
      </DialogPrimitive.Close>
    )
  }
  return (
    <DialogPrimitive.Close
      data-slot="dialog-close"
      asChild={asChild}
      {...props}
    >
      {children}
    </DialogPrimitive.Close>
  )
}

function DialogOverlay({
  className,
  onClick,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Overlay>) {
  const dismiss = React.useContext(DialogDismissContext)
  return (
    <DialogPrimitive.Overlay
      data-slot="dialog-overlay"
      onClick={(e) => {
        onClick?.(e)
        if (!e.defaultPrevented && e.target === e.currentTarget) {
          dismiss?.()
        }
      }}
      className={cn(
        "fixed inset-0 z-[9990] bg-black/55 backdrop-blur-[2px] transition-opacity duration-150 data-[state=open]:opacity-100 data-[state=closed]:opacity-0",
        className
      )}
      {...props}
    />
  )
}

function DialogContent({
  className,
  children,
  showCloseButton = true,
  onPointerDownOutside,
  onInteractOutside,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Content> & {
  showCloseButton?: boolean
}) {
  const dismiss = React.useContext(DialogDismissContext)
  return (
    <DialogPortal>
      <DialogOverlay />
      <DialogPrimitive.Content
        data-slot="dialog-content"
        onPointerDownOutside={(e) => {
          onPointerDownOutside?.(e)
          if (!e.defaultPrevented) dismiss?.()
        }}
        onInteractOutside={(e) => {
          onInteractOutside?.(e)
          if (!e.defaultPrevented) dismiss?.()
        }}
        className={cn(
          "fixed top-1/2 left-1/2 z-[9995] grid w-full max-w-[calc(100%-2rem)] -translate-x-1/2 -translate-y-1/2 gap-4 rounded-xl border border-border bg-card p-5 text-card-foreground shadow-xl outline-none transition-all duration-150 sm:max-w-md",
          className
        )}
        {...props}
      >
        {children}
        {showCloseButton && (
          <DialogPrimitive.Close
            data-slot="dialog-close"
            aria-label="Close"
            className="absolute top-3.5 right-3.5 rounded-md p-1 text-muted-foreground opacity-75 transition-opacity hover:bg-accent hover:text-foreground hover:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none cursor-pointer"
          >
            <XIcon className="size-4" />
            <span className="sr-only">Close</span>
          </DialogPrimitive.Close>
        )}
      </DialogPrimitive.Content>
    </DialogPortal>
  )
}

function DialogHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-header"
      className={cn("flex flex-col gap-1.5 text-left", className)}
      {...props}
    />
  )
}

function DialogFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-footer"
      className={cn(
        "flex flex-col-reverse gap-2 sm:flex-row sm:justify-end",
        className
      )}
      {...props}
    />
  )
}

function DialogTitle({
  className,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Title>) {
  return (
    <DialogPrimitive.Title
      data-slot="dialog-title"
      className={cn("text-base leading-none font-semibold tracking-tight", className)}
      {...props}
    />
  )
}

function DialogDescription({
  className,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Description>) {
  return (
    <DialogPrimitive.Description
      data-slot="dialog-description"
      className={cn("text-xs text-muted-foreground leading-relaxed", className)}
      {...props}
    />
  )
}

export {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogOverlay,
  DialogPortal,
  DialogTitle,
  DialogTrigger,
}
