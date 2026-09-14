import { command, useDesktop } from "@/lib/bridge";
import { Toaster } from "@/components/ui/sonner";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { StatusModule } from "@/components/sku/StatusModule";
import { MissingModule } from "@/components/sku/MissingModule";
import { AvailabilityModule } from "@/components/sku/AvailabilityModule";
import { ImagesModule } from "@/components/sku/ImagesModule";
import { Button } from "@/components/ui/button";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Boxes, Minus, RefreshCw, Square, X } from "lucide-react";
import { cn } from "@/lib/utils";

export default function App() {
  const desktop=useDesktop();
  const VERSION=desktop?.version ?? "";
  const [module, setModule] = useState<"status" | "missing" | "availability" | "images">("status");
  const [closeOpen, setCloseOpen] = useState(false);
  const [restartOpen, setRestartOpen] = useState(false);
  const [updating, setUpdating] = useState(false);

  useEffect(()=>{
    function receive(event:any){ const data=event.detail;
      if(data.event==='close')setCloseOpen(true);
      if(data.event==='updateStatus')toast.info(data.message,{id:'upd'});
      if(data.event==='updateReady')setRestartOpen(true);
      if(data.event==='updateBusy')setUpdating(data.busy);
    }
    window.addEventListener('desktop-event',receive);return()=>window.removeEventListener('desktop-event',receive);
  },[]);
  async function checkUpdates(){setUpdating(true);try{await command('app','updates');}finally{setUpdating(false);}}
  if(!desktop)return <main className="p-6 text-sm">Завантаження інтерфейсу SKU Майстер…<Toaster /></main>;
  return (
    <main className="min-h-screen bg-background p-0">
      <Toaster />
      <div className="flex min-h-screen flex-col overflow-hidden bg-surface">
        {/* Заголовок вікна */}
        <div onPointerDown={(e)=>{if(!(e.target as HTMLElement).closest("button"))void command("app","drag");}} onDoubleClick={(e)=>{if(!(e.target as HTMLElement).closest("button"))void command("app","maximize");}} className="select-none flex items-center justify-between gap-4 border-b border-border bg-panel px-4 py-2.5">
          <div className="flex items-center gap-2.5">
            <span className="flex size-7 items-center justify-center rounded-md bg-primary text-primary-foreground">
              <Boxes className="size-4" />
            </span>
            <h1 className="text-sm font-semibold tracking-tight">SKU Майстер</h1>
            <span className="rounded border border-border px-1.5 py-0.5 text-[10px] text-muted-foreground">
              версія {VERSION}
            </span>
          </div>
          <div className="flex items-center gap-1">
            <Button variant="ghost" size="sm" onClick={checkUpdates} disabled={updating}>
              <RefreshCw className={cn("size-3.5", updating && "animate-spin")} /> Оновлення
            </Button>
            <span className="mx-1 h-5 w-px bg-border" />
            <button className="rounded p-1.5 text-muted-foreground hover:bg-muted" aria-label="Згорнути" onClick={()=>void command("app","minimize")}>
              <Minus className="size-3.5" />
            </button>
            <button className="rounded p-1.5 text-muted-foreground hover:bg-muted" aria-label="Розгорнути" onClick={()=>void command("app","maximize")}>
              <Square className="size-3" />
            </button>
            <button
              onClick={() => void command("app","close")}
              className="rounded p-1.5 text-muted-foreground hover:bg-destructive hover:text-destructive-foreground"
              aria-label="Закрити"
            >
              <X className="size-3.5" />
            </button>
          </div>
        </div>

        {/* Перемикач модулів */}
        <div className="flex items-center gap-1 border-b border-border bg-panel px-4 pb-0">
          {(
            [
              ["status", "Оновлення статусів"],
              ["missing", "Товари, яких немає на сайті"],
              ["availability", "Перевірка наявності"],
              ["images", "Зображення"],
            ] as const
          ).map(([id, label]) => (
            <button
              key={id}
              onClick={() => setModule(id)}
              className={cn(
                "-mb-px border-b-2 px-3 py-2.5 text-xs font-medium transition-colors",
                module === id
                  ? "border-primary text-foreground"
                  : "border-transparent text-muted-foreground hover:text-foreground",
              )}
            >
              {label}
            </button>
          ))}
        </div>

        <div className="flex-1 bg-surface p-4 lg:p-5">
          <div className={module === "status" ? "" : "hidden"}>
            <StatusModule />
          </div>
          <div className={module === "missing" ? "" : "hidden"}>
            <MissingModule />
          </div>
          <div className={module === "availability" ? "" : "hidden"}>
            <AvailabilityModule />
          </div>
          <div className={module === "images" ? "" : "hidden"}><ImagesModule /></div>
        </div>

        <footer className="flex items-center justify-between border-t border-border bg-panel px-4 py-2 text-[11px] text-muted-foreground">
          <span>Обробка — локальна. Інтернет потрібен для завантажень та оновлень.</span>
          <span>Модулі працюють незалежно — перемикання зберігає поточний прогрес.</span>
        </footer>
      </div>

      <AlertDialog open={closeOpen} onOpenChange={setCloseOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Закрити програму?</AlertDialogTitle>
            <AlertDialogDescription>
              Результат не збережено. Якщо закрити зараз, поточні результати перевірки буде втрачено.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Повернутися</AlertDialogCancel>
            <AlertDialogAction onClick={() => void command("app","closeConfirmed")}>Закрити без збереження</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={restartOpen} onOpenChange={setRestartOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Перезапустити для встановлення оновлення?</AlertDialogTitle>
            <AlertDialogDescription>
              Оновлення завантажено. Програма перезапуститься після підтвердження.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Пізніше</AlertDialogCancel>
            <AlertDialogAction onClick={() => void command("app","restart")}>Перезапустити</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </main>
  );
}
