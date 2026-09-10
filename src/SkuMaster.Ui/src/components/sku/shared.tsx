import { type ReactNode } from "react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { command } from "@/lib/bridge";
import { toast } from "sonner";
import { Copy, FileSpreadsheet, FileText, FolderOpen } from "lucide-react";

export function Panel({
  title,
  description,
  actions,
  children,
  className,
}: {
  title?: string;
  description?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section
      className={cn(
        "rounded-lg border border-border bg-panel text-panel-foreground shadow-[var(--shadow-panel)]",
        className,
      )}
    >
      {(title || actions) && (
        <header className="flex items-start justify-between gap-4 border-b border-border px-4 py-3">
          <div>
            {title && <h2 className="text-sm font-semibold tracking-tight">{title}</h2>}
            {description && <p className="mt-0.5 text-xs text-muted-foreground">{description}</p>}
          </div>
          {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </header>
      )}
      <div className="p-4">{children}</div>
    </section>
  );
}

export function FieldRow({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className="grid gap-1.5">
      <span className="text-xs font-medium text-foreground">{label}</span>
      {children}
      {hint && <span className="text-[11px] leading-snug text-muted-foreground">{hint}</span>}
    </label>
  );
}

export function FileField({
  label,
  format,
  value,
  optional,
  buttonLabel = "Обрати…",
  onPick,
  disabled,
}: {
  label: string;
  format: string;
  value: string | null;
  optional?: boolean;
  buttonLabel?: string;
  onPick: () => void;
  disabled?: boolean;
}) {
  const Icon = format.toUpperCase().includes("CSV") ? FileText : FileSpreadsheet;
  return (
    <div className="grid min-w-0 gap-1.5">
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-xs font-medium">
          {label}
          {!optional && <span className="ml-1 text-destructive">*</span>}
        </span>
        <span className="rounded border border-border bg-muted px-1.5 py-0.5 text-[10px] font-medium tracking-wide text-muted-foreground uppercase">
          {format}
        </span>
      </div>
      <div className="flex min-w-0 gap-2">
        <div
          className={cn(
            "flex h-9 min-w-0 flex-1 items-center gap-2 rounded-md border border-input bg-background px-3 text-xs",
            value ? "text-foreground" : "text-muted-foreground",
          )}
        >
          <Icon className="size-3.5 shrink-0 opacity-60" />
          <span className="min-w-0 truncate" title={value ?? ""}>{value || "Файл не обрано"}</span>
        </div>
        <Button type="button" variant="secondary" size="sm" onClick={onPick} disabled={disabled}>
          <FolderOpen className="size-3.5" />
          {buttonLabel}
        </Button>
      </div>
    </div>
  );
}

export function StatBlock({
  label,
  value,
  active,
  tone = "neutral",
  onClick,
}: {
  label: string;
  value: number;
  active?: boolean;
  tone?: "neutral" | "danger" | "success" | "primary" | "muted";
  onClick: () => void;
}) {
  const toneBar: Record<string, string> = {
    neutral: "bg-info",
    danger: "bg-destructive",
    success: "bg-success",
    primary: "bg-primary",
    muted: "bg-muted-foreground",
  };
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        "group relative overflow-hidden rounded-lg border bg-panel px-3 py-2.5 text-left transition-all",
        active
          ? "border-primary ring-2 ring-ring/25"
          : "border-border hover:border-primary/40 hover:shadow-[var(--shadow-panel)]",
      )}
    >
      <span className={cn("absolute inset-y-0 left-0 w-1", toneBar[tone])} />
      <span className="block pl-2 text-[11px] leading-tight font-medium text-muted-foreground">{label}</span>
      <span className="block pl-2 text-xl font-semibold tabular-nums">{value.toLocaleString("uk-UA")}</span>
    </button>
  );
}

export function CopySku({ sku }: { sku: string }) {
  return (
    <button
      type="button"
      title="Натисніть, щоб скопіювати"
      onClick={async () => { const response=await command("app","copy",{text:sku}); if(!response?.failed)toast.success("Артикул скопійовано",{description:sku}); }}
      className="group inline-flex items-center gap-1.5 rounded px-1 py-0.5 mono-sku hover:bg-accent hover:text-accent-foreground"
    >
      {sku}
      <Copy className="size-3 opacity-0 transition-opacity group-hover:opacity-70" />
    </button>
  );
}

export function StateBanner({
  tone = "info",
  children,
}: {
  tone?: "info" | "success" | "warning" | "danger";
  children: ReactNode;
}) {
  const map = {
    info: "border-info/30 bg-info/8 text-info",
    success: "border-success/30 bg-success/10 text-success",
    warning: "border-warning/40 bg-warning/12 text-warning-foreground",
    danger: "border-destructive/30 bg-destructive/8 text-destructive",
  } as const;
  return (
    <div className={cn("rounded-md border px-3 py-2 text-xs leading-relaxed", map[tone])}>{children}</div>
  );
}

export function SearchInput({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
}) {
  return (
    <Input
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      className="h-9 text-xs"
    />
  );
}

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-1 px-4 py-14 text-center">
      <p className="text-sm font-medium">{title}</p>
      {hint && <p className="max-w-sm text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}

export function FolderField({value,onChange}:{value:string;onChange:(v:string)=>void}) {
  return <div className="flex gap-2"><Input value={value} onChange={e=>onChange(e.target.value)} className="h-9 text-xs"/><Button variant="secondary" size="sm" onClick={async()=>{const response=await command('app','folder');if(response?.path)onChange(response.path);}}><FolderOpen className="size-3.5"/>Обрати…</Button></div>;
}
export function Pages({count,page,setPage,hint}:{count:number;page:number;setPage:(v:number)=>void;hint?:string}) {
  if(count<=200 && !hint)return null;
  return <div className="mt-2 flex items-center justify-between gap-3 text-[11px] text-muted-foreground">
    {hint ? <p className="min-w-0 flex-1 truncate" title={hint}>{hint}</p> : <span />}
    {count>200 && <div className="flex shrink-0 items-center gap-2 whitespace-nowrap text-xs"><Button size="sm" variant="ghost" disabled={page===0} onClick={()=>setPage(page-1)}>Назад</Button><span>Сторінка {page+1} із {Math.ceil(count/200)}</span><Button size="sm" variant="ghost" disabled={(page+1)*200>=count} onClick={()=>setPage(page+1)}>Далі</Button></div>}
  </div>;
}
