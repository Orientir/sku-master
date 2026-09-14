import { HelpGuide } from "./shared";
import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import { command, useDesktop, saveFile, changeSetting } from "@/lib/bridge";
import { Pages, FolderField, Panel, FieldRow, FileField, StatBlock, CopySku, StateBanner, SearchInput, EmptyState } from "./shared";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Progress } from "@/components/ui/progress";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { AlertTriangle, Download, LoaderCircle, Play, Search, Trash2, Upload, X } from "lucide-react";

type Block = "all" | "onsite" | "excluded" | "addable";

export function MissingModule() {
  const desktop=useDesktop(), state=desktop.missing, settings=state.settings;
  const files=state.files, processing=state.busy, items=state.hasResult ? state.rows : null;
  const exceptions: string[]=state.exceptions, stats=state.stats, warnings=state.warnings;
  const [tab,setTab]=useState('files'), [block,setBlock]=useState<Block>('addable');
  const [search,updateSearch]=useState(''), [excSearch,setExcSearch]=useState('');
  const [selected,setSelected]=useState<string[]>([]);
  const [scope,setScope]=useState<Set<string>>(new Set());
  const [saveOpen,setSaveOpen]=useState<null|'result'|'exceptions'>(null);
  const [fileName,setFileName]=useState('tovary-dlya-dodavannya');
  const [folder,setFolder]=useState(desktop.documents);
  const [page,setPage]=useState(0);
  const [exceptionPage,setExceptionPage]=useState(0);
  const [loadingExceptions,setLoadingExceptions]=useState(false);
  const [exceptionRows,setExceptionRows]=useState<string[]>([]);
  useEffect(()=>{
    if(tab!=='exceptions')return;
    setLoadingExceptions(true);
    // Yield a paint before preparing the list, and discard obsolete searches.
    let timer: ReturnType<typeof setTimeout>;
    const frame=requestAnimationFrame(()=>{timer=setTimeout(()=>{
      const query=excSearch.trim().toLowerCase();
      setExceptionRows(exceptions.filter(s=>s.toLowerCase().includes(query)));
      setExceptionPage(0);
      setLoadingExceptions(false);
    },0);});
    return()=>{cancelAnimationFrame(frame);clearTimeout(timer);};
  },[tab,exceptions,excSearch]);
  function changeTab(value:string){if(value==='exceptions' && tab!==value)setLoadingExceptions(true);setTab(value);}
  const outFormat=settings.outputFormat, stale=false, ready=state.canAnalyze;
  const change=(section:string,key:string,value:any)=>void changeSetting('missing',section,key,value);
  const setOutFormat=(v:string)=>void changeSetting('missing','','outputFormat',v);
  const classified=state.rows;
  function refreshScope(b:Block, rows=classified) {setScope(new Set(rows.filter(i=> b==='all' || (b==='onsite' && i.onSite) || (b==='excluded' && !i.onSite && i.excluded) || (b==='addable' && !i.onSite && !i.excluded)).map(i=>i.sku)));setPage(0);}
  useEffect(()=>{if(state.hasResult)refreshScope(block);},[state.hasResult]);
  function selectBlock(b:Block){setBlock(b);updateSearch('');refreshScope(b);}
  function setSearch(q:string){updateSearch(q);refreshScope(block);}
  const visible=classified.filter(i=>scope.has(i.sku) && `${i.sku} ${i.name}`.toLowerCase().includes(search.trim().toLowerCase()));
  const filteredExceptions=exceptionRows;
  async function pick(source:string){ const response=await command('missing','pick',{source}); if(source==='exceptions' && !response?.failed && !response?.cancelled){setSelected([]);setBlock('addable');refreshScope('addable',response.rows);toast.success('Список виключень оновлено');} }
  async function run(){const response=await command('missing','analyze');if(!response?.failed){setBlock('addable');updateSearch('');setTab('result');refreshScope('addable',response.rows);}}
  function cancel(){void command('missing','cancel');}
  function toggleException(sku:string,value:boolean){void command('missing','exclude',{sku,value});}
  useEffect(()=>{setFileName(saveOpen==='exceptions'?'vyklyuchennya':'tovary-dlya-dodavannya');},[saveOpen]);
  return (
    <Tabs value={tab} onValueChange={changeTab} className="gap-4">
      <TabsList className="w-full justify-start overflow-x-auto">
        <TabsTrigger value="files">Файли</TabsTrigger>
        <TabsTrigger value="settings">Налаштування</TabsTrigger>
        <TabsTrigger value="result">Результат</TabsTrigger>
        <TabsTrigger value="exceptions">
          Виключення
          <span className="ml-1.5 rounded bg-muted px-1.5 text-[10px] font-semibold text-muted-foreground">
            {exceptions.length}
          </span>
        </TabsTrigger>
        <TabsTrigger value="warnings">
          Попередження
          {warnings.length > 0 && (
            <span className="ml-1.5 rounded bg-warning/25 px-1.5 text-[10px] font-semibold text-warning-foreground">
              {warnings.length}
            </span>
          )}
        </TabsTrigger>
        <TabsTrigger value="help">Як користуватися</TabsTrigger>
      </TabsList>

      <TabsContent value="files" className="space-y-4">
        <Panel title="Файли" description="Файли сайту й постачальника обов'язкові. Виключення — за потреби.">
          <div className="grid gap-4 lg:grid-cols-2">
            <FileField
              label="Залишки на сайті"
              format="CSV"
              value={files.site}
              onPick={() => pick("site")}
              disabled={processing}
            />
            <FileField
              label="Постачальник"
              format="XLS / XLSX"
              value={files.supplier}
              onPick={() => pick("supplier")}
              disabled={processing}
            />
          </div>

          <div className="mt-4 rounded-md border border-border bg-surface p-3">
            <FileField
              label="Виключення"
              format="XLS / XLSX"
              optional
              buttonLabel="Імпортувати…"
              value={files.exceptions}
              onPick={() => pick("exceptions")}
              disabled={processing}
            />
            <p className="mt-2 text-[11px] leading-relaxed text-muted-foreground">
              Збережено виключень: <strong className="text-foreground">{exceptions.length}</strong>. Файл не потрібно
              завантажувати щоразу — програма використовує збережений список. Новий імпорт повністю замінює поточний
              список.
            </p>
          </div>

          <div className="mt-4 space-y-3">
            <StateBanner>{state.message}</StateBanner>
            {processing ? (
              <div className="flex items-center gap-3">
                <Progress className="h-2 flex-1 animate-pulse" />
                <span className="text-xs tabular-nums text-muted-foreground">Обробка…</span>
                <Button variant="outline" size="sm" onClick={cancel}>
                  <X className="size-3.5" /> Скасувати
                </Button>
              </div>
            ) : (
              <Button onClick={run} disabled={!ready}>
                <Play className="size-4" /> Знайти товари
              </Button>
            )}
          </div>
        </Panel>
      </TabsContent>

      <TabsContent value="settings" className="space-y-4"><fieldset disabled={processing}>
        <div className="grid gap-4 lg:grid-cols-3">
          <Panel title="Залишки на сайті (CSV)">
            <div className="grid gap-3">
              <FieldRow label="Номер стовпця з артикулом">
                <Input className="h-9 text-xs" type="number" min="1" value={settings.site.skuColumn} onChange={(e)=>{if(Number(e.target.value)>0)change("site","skuColumn",Number(e.target.value));}} />
              </FieldRow>
              <FieldRow label="Перший рядок із даними" hint="Якщо є заголовки — вкажіть 2.">
                <Input className="h-9 text-xs" type="number" min="1" value={settings.site.firstDataRow} onChange={(e)=>{if(Number(e.target.value)>0)change("site","firstDataRow",Number(e.target.value));}} />
              </FieldRow>
              <FieldRow label="Роздільник">
                <Mini value={settings.site.delimiter} onChange={(v)=>change("site","delimiter",v)} options={[["auto", "Авто"], [";", "Крапка з комою"], [",", "Кома"], ["\t", "Табуляція"]]} />
              </FieldRow>
              <FieldRow label="Кодування">
                <Mini value={settings.site.encodingName} onChange={(v)=>change("site","encodingName",v)} options={[["auto", "Авто"], ["utf-8", "UTF-8"], ["windows-1251", "Windows-1251"]]} />
              </FieldRow>
            </div>
          </Panel>
          <Panel title="Постачальник (Excel)">
            <div className="grid gap-3">
              <FieldRow label="Номер стовпця з артикулом">
                <Input className="h-9 text-xs" type="number" min="1" value={settings.supplier.skuColumn} onChange={(e)=>{if(Number(e.target.value)>0)change("supplier","skuColumn",Number(e.target.value));}} />
              </FieldRow>
              <FieldRow label="Номер стовпця з назвою">
                <Input className="h-9 text-xs" type="number" min="1" value={settings.supplier.nameColumn} onChange={(e)=>{if(Number(e.target.value)>0)change("supplier","nameColumn",Number(e.target.value));}} />
              </FieldRow>
              <FieldRow label="Перший рядок із даними">
                <Input className="h-9 text-xs" type="number" min="1" value={settings.supplier.firstDataRow} onChange={(e)=>{if(Number(e.target.value)>0)change("supplier","firstDataRow",Number(e.target.value));}} />
              </FieldRow>
              <FieldRow label="Аркуш" hint="Порожньо — перший аркуш.">
                <Input className="h-9 text-xs" placeholder="Наприклад: Прайс" value={settings.supplier.sheetName} onChange={(e)=>change("supplier","sheetName",e.target.value)} />
              </FieldRow>
            </div>
          </Panel>
          <Panel title="Виключення (Excel)">
            <div className="grid gap-3">
              <FieldRow label="Номер стовпця з артикулом">
                <Input className="h-9 text-xs" type="number" min="1" value={settings.exclusions.skuColumn} onChange={(e)=>{if(Number(e.target.value)>0)change("exclusions","skuColumn",Number(e.target.value));}} />
              </FieldRow>
              <FieldRow label="Перший рядок із даними">
                <Input className="h-9 text-xs" type="number" min="1" value={settings.exclusions.firstDataRow} onChange={(e)=>{if(Number(e.target.value)>0)change("exclusions","firstDataRow",Number(e.target.value));}} />
              </FieldRow>
              <FieldRow label="Аркуш">
                <Input className="h-9 text-xs" placeholder="Наприклад: Лист1" value={settings.exclusions.sheetName} onChange={(e)=>change("exclusions","sheetName",e.target.value)} />
              </FieldRow>
              <FieldRow label="Формат вивантаження" hint="Застосовується і до результату, і до файлу виключень.">
                <Mini value={outFormat} onChange={setOutFormat} options={[["csv", "CSV"], ["xlsx", "XLSX"]]} />
              </FieldRow>
            </div>
          </Panel>
        </div>
      </fieldset></TabsContent>

      <TabsContent value="result" className="space-y-4">
        {!items ? (
          <Panel>
            <EmptyState title="Результату ще немає" hint="Оберіть файли та натисніть «Знайти товари»." />
          </Panel>
        ) : (
          <>
            <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
              <StatBlock label="У постачальника" value={stats.all} tone="neutral" active={block === "all"} onClick={() => selectBlock("all")} />
              <StatBlock label="Уже є на сайті" value={stats.onsite} tone="muted" active={block === "onsite"} onClick={() => selectBlock("onsite")} />
              <StatBlock label="Виключено з пошуку" value={stats.excluded} tone="danger" active={block === "excluded"} onClick={() => selectBlock("excluded")} />
              <StatBlock label="Можна додати на сайт" value={stats.addable} tone="success" active={block === "addable"} onClick={() => selectBlock("addable")} />
            </div>

            <Panel
              title="Знайдені товари"
              description={`Показано ${visible.length} рядків · до вивантаження: ${stats.addable}`}
            >
              <div className="max-w-md">
                <SearchInput value={search} onChange={setSearch} placeholder="Пошук за артикулом або назвою…" />
              </div>
              <div className="mt-4 overflow-hidden rounded-md border border-border">
                <div className="max-h-[max(120px,min(460px,calc(100dvh-540px)))] overflow-auto">
                  <table className="w-full text-xs">
                    <thead className="sticky top-0 z-10 bg-surface text-muted-foreground">
                      <tr className="[&>th]:px-3 [&>th]:py-2 [&>th]:text-left [&>th]:font-medium">
                        <th className="w-24">Виключити</th>
                        <th className="w-44">Артикул</th>
                        <th>Назва</th>
                      </tr>
                    </thead>
                    <tbody>
                      {visible.length === 0 ? (
                        <tr>
                          <td colSpan={3}>
                            <EmptyState title="Нічого не знайдено за вибраними умовами" />
                          </td>
                        </tr>
                      ) : (
                        visible.slice(page * 200, (page + 1) * 200).map((i) => (
                          <tr key={i.sku} className="border-t border-border hover:bg-surface/70">
                            <td className="px-3 py-1.5">
                              <Checkbox
                                checked={i.excluded}
                                disabled={i.onSite}
                                onCheckedChange={(v) => toggleException(i.sku, Boolean(v))}
                              />
                            </td>
                            <td className="px-2 py-1.5">
                              <CopySku sku={i.sku} />
                            </td>
                            <td className="px-3 py-1.5">
                              {i.name}
                              {i.onSite && <span className="ml-2 text-[11px] text-muted-foreground">уже є на сайті</span>}
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
              <Pages count={visible.length} page={page} setPage={setPage} hint="Позначка «Виключити» зберігається одразу. Рядок залишається видимим, доки ви не зміните фільтр або пошук — це дозволяє скасувати випадковий клік." />
            </Panel>
            <Panel><div className="flex items-center justify-end gap-3">
              <span className="text-xs text-muted-foreground">До файлу потрапить {stats.addable.toLocaleString('uk-UA')} товарів</span>
              <Button onClick={()=>setSaveOpen('result')} disabled={processing || !state.canSave}><Download className="size-4" /> Зберегти результат…</Button>
            </div></Panel>
          </>
        )}
      </TabsContent>

      <TabsContent value="exceptions">
        <Panel
          title={`Виключення — ${exceptions.length}`}
          description="Список автоматично застосовується до кожного пошуку."
          actions={
            <>
              <Button variant="secondary" size="sm" onClick={() => pick("exceptions")}>
                <Upload className="size-3.5" /> Імпортувати…
              </Button>
              <Button size="sm" onClick={() => setSaveOpen("exceptions")} disabled={exceptions.length === 0}>
                <Download className="size-3.5" /> Зберегти виключення…
              </Button>
            </>
          }
        >
          <div className="flex flex-wrap items-center gap-3">
            <div className="max-w-xs flex-1">
              <SearchInput value={excSearch} onChange={setExcSearch} placeholder="Пошук за артикулом…" />
            </div>
            <Button
              variant="outline"
              size="sm"
              disabled={selected.length === 0}
              onClick={() => {
                void command("missing","remove",{skus:selected});
                setSelected([]);
              }}
            >
              <Trash2 className="size-3.5" /> Прибрати вибрані ({selected.length})
            </Button>
          </div>

          <div className="mt-4 overflow-hidden rounded-md border border-border">
            {loadingExceptions ? <div role="status" className="flex items-center justify-center gap-2 py-14 text-xs text-muted-foreground"><LoaderCircle className="size-5 animate-spin" />Завантаження виключень…</div> : filteredExceptions.length === 0 ? (
              <EmptyState
                title={exceptions.length === 0 ? "Список виключень порожній" : "Нічого не знайдено"}
                hint="Позначте товар у результаті пошуку, щоб додати його сюди."
              />
            ) : (
              <ul className="max-h-[max(120px,min(420px,calc(100dvh-440px)))] divide-y divide-border overflow-auto">
                {filteredExceptions.slice(exceptionPage*200,(exceptionPage+1)*200).map((sku) => (
                  <li key={sku} className="flex items-center gap-3 px-3 py-1.5 hover:bg-surface/70">
                    <Checkbox
                      checked={selected.includes(sku)}
                      onCheckedChange={(v) =>
                        setSelected((s) => (v ? [...s, sku] : s.filter((x) => x !== sku)))
                      }
                    />
                    <CopySku sku={sku} />
                  </li>
                ))}
              </ul>
            )}
          </div>
          {!loadingExceptions && <Pages count={filteredExceptions.length} page={exceptionPage} setPage={setExceptionPage} />}
          <p className="mt-3 text-[11px] leading-relaxed text-muted-foreground">
            Видалення застосовуються миттєво. Новий імпорт замінює весь список, включно з доданими через позначки. У разі
            помилки імпорту поточний список залишається без змін. У файл зберігаються всі виключення — незалежно від
            пошуку. Файл має один стовпець: «Артикул».
          </p>
        </Panel>
      </TabsContent>

      <TabsContent value="warnings">
        <Panel title={`Попередження — ${warnings.length}`}>
          {warnings.length === 0 ? (
            <EmptyState title="Попереджень немає" />
          ) : (
            <ul className="max-h-[420px] space-y-2 overflow-auto">
              {warnings.map((w, i) => (
                <li key={i} className="flex gap-2 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs leading-relaxed">
                  <AlertTriangle className="mt-0.5 size-3.5 shrink-0 text-warning" />
                  <span>{w}</span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </TabsContent>

      <TabsContent value="help">
        <HelpGuide steps={[
              ["Завантажте файли", "Додайте залишки сайту (CSV) та файл постачальника (XLS/XLSX): артикул і назва товару. За замовчуванням перший рядок містить дані. Колонки, перший рядок і аркуш можна змінити в налаштуваннях."],
              ["Додайте виключення за потреби", "Імпортуйте Excel зі списком артикулів, які не потрібно шукати. Список зберігається між запусками й оновленнями програми. Новий імпорт повністю замінює попередні виключення."],
              ["Знайдіть товари", "Натисніть «Знайти товари». Програма знайде товари постачальника, яких немає на сайті та у виключеннях."],
              ["Перегляньте результат", "Натискайте блоки статистики, користуйтеся пошуком і сторінками. Клік по артикулу копіює його. Повідомлення про файли доступні на вкладці «Попередження»."],
              ["Позначте зайві товари", "Галочка «Виключити» одразу зберігає артикул у виключеннях. Рядок залишається видимим до зміни фільтра або пошуку, щоб ви могли зняти випадкову галочку. Наступного разу цей товар не потрапить до списку для додавання."],
              ["Керуйте виключеннями", "На вкладці «Виключення» можна шукати й копіювати артикули, позначати їх для видалення та зберігати весь список у файл. Пошук працює по всіх виключеннях, незалежно від поточної сторінки."],
              ["Збережіть результат", "Оберіть CSV або XLSX у налаштуваннях, натисніть «Зберегти результат…» унизу та вкажіть папку й назву файлу. Будуть збережені артикули та назви всіх товарів для додавання на сайт — незалежно від фільтра, пошуку чи сторінки."],
            ]} />
      </TabsContent>

      <Dialog open={saveOpen !== null} onOpenChange={(o) => !o && setSaveOpen(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {saveOpen === "exceptions" ? "Зберегти виключення" : "Зберегти знайдені товари"}
            </DialogTitle>
            <DialogDescription>
              {saveOpen === "exceptions"
                ? `Один стовпець «Артикул» · ${exceptions.length} записів.`
                : `Стовпці «Артикул» і «Назва» · ${stats.addable} товарів, які можна додати на сайт.`}
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-3">
            <FieldRow label="Назва файлу">
              <Input
                className="h-9 text-xs"
                value={fileName} onChange={(e)=>setFileName(e.target.value)}
              />
            </FieldRow>
            <FieldRow label="Папка">
              <FolderField value={folder} onChange={setFolder} />
            </FieldRow>
            <FieldRow label="Формат">
              <Mini value={outFormat} onChange={setOutFormat} options={[["csv", "CSV"], ["xlsx", "XLSX"]]} />
            </FieldRow>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setSaveOpen(null)}>
              Скасувати
            </Button>
            <Button
              disabled={processing} onClick={async ()=>{ const response=await saveFile("missing",saveOpen==="exceptions"?"saveExclusions":"save",folder,fileName); if(!response?.failed && !response?.cancelled){setSaveOpen(null);toast.success("Файл збережено");} }}
            >
              <Search className="hidden" /> Зберегти
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Tabs>
  );
}

function Mini({
  value,
  onChange,
  options,
}: {
  value: string;
  onChange?: (v: string) => void;
  options: [string, string][];
}) {
  return (
    <Select value={value} onValueChange={onChange ?? (() => {})}>
      <SelectTrigger className="h-9 w-full text-xs">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {options.map(([v, l]) => (
          <SelectItem key={v} value={v}>
            {l}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
