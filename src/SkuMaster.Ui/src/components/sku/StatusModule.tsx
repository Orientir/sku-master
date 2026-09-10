import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import { STATUSES, NO_STATUS, type Status } from "@/lib/sku-data";
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
import { AlertTriangle, Play, RotateCcw, Save, X } from "lucide-react";

const CLEAR = "__clear__";
const ALL = "__all__";

type Filter = "all" | "unavailable" | "again" | "changed" | "unchanged";

export function StatusModule() {
  const desktop = useDesktop();
  const state = desktop.status;
  const settings = state.settings;
  const files = state.files;
  const processing = state.busy;
  const result = state.hasResult ? state.rows : null;
  const computed = state.rows;
  const stats = state.stats;
  const stale = false;
  function change(section: string, key: string, value: any) {
    void changeSetting('status', section, key, value);
  }
  const missingStatus = settings.rules.unavailableStatus, replaceStatus = settings.rules.matchStatus;
  const clearFound = settings.rules.clearFoundStatus, foundNewStatus = settings.rules.replacementStatus;
  const setMissingStatus = (v: string) => change('rules','unavailableStatus',v);
  const setReplaceStatus = (v: string) => change('rules','matchStatus',v);
  const setClearFound = (v: boolean) => change('rules','clearFoundStatus',v);
  const setFoundNewStatus = (v: string) => change('rules','replacementStatus',v);
  const csvHeaders=settings.csvInput.hasHeader, delimiter=settings.csvInput.delimiter, encoding=settings.csvInput.encodingName;
  const setCsvHeaders=(v:boolean)=>change('csvInput','hasHeader',v);
  const setDelimiter=(v:string)=>change('csvInput','delimiter',v);
  const setEncoding=(v:string)=>change('csvInput','encodingName',v);
  const outFormat=settings.export.format, outHeader=settings.export.includeHeaders, onlyChanged=settings.export.onlyChanged;
  const setOutFormat=(v:string)=>change('export','format',v);
  const setOutHeader=(v:boolean)=>change('export','includeHeaders',v);
  const setOnlyChanged=(v:boolean)=>change('export','onlyChanged',v);
  const [tab, setTab] = useState('files');
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [fCurrent, setFCurrent] = useState(ALL), [fNew, setFNew] = useState(ALL);
  const [saveOpen, setSaveOpen] = useState(false);
  const [fileName, setFileName] = useState('export-onovleni-statusy');
  const [folder, setFolder] = useState(desktop.documents);
  const ready = Boolean(files.site && files.oneC && files.supplier);

  function pick(source: string) { void command('status','pick',{source}); }
  async function run() {
    const response = await command('status','analyze');
    if (!response?.failed) { resetFilters(); setTab('result'); }
  }
  function cancel() { void command('status','cancel'); }
  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    return computed.filter((r) => {
      if (filter === "unavailable" && !r.becameUnavailable) return false;
      if (filter === "again" && !r.againAvailable) return false;
      if (filter === "changed" && !r.changed) return false;
      if (filter === "unchanged" && r.changed) return false;
      if (fCurrent !== ALL && (r.current || NO_STATUS) !== fCurrent) return false;
      if (fNew !== ALL && (r.next || NO_STATUS) !== fNew) return false;
      if (q) {
        const hay = `${r.sku} ${r.current || NO_STATUS} ${r.next || NO_STATUS}`.toLowerCase();
        if (!hay.includes(q)) return false;
      }
      return true;
    });
  }, [computed, search, filter, fCurrent, fNew]);

  const [page,setPage]=useState(0);
  useEffect(()=>setPage(0),[search,filter,fCurrent,fNew,result === null]);
  useEffect(()=>setPage(p=>Math.min(p,Math.max(0,Math.ceil(visible.length/200)-1))),[visible.length]);
  const uniqueSku = useMemo(() => new Set(visible.map((r) => r.sku)).size, [visible]);

  const warnings = state.warnings;

  function selectBlock(f: Filter) {
    setFilter(f);
    setSearch("");
    setFCurrent(ALL);
    setFNew(ALL);
  }

  function resetFilters() {
    setFilter("all");
    setSearch("");
    setFCurrent(ALL);
    setFNew(ALL);
  }

  const exportCount = onlyChanged ? stats.changed : stats.checked;

  return (
    <Tabs value={tab} onValueChange={setTab} className="gap-4">
      <TabsList className="w-full justify-start overflow-x-auto">
        <TabsTrigger value="files">Файли</TabsTrigger>
        <TabsTrigger value="settings">Налаштування</TabsTrigger>
        <TabsTrigger value="result">Результат</TabsTrigger>
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
        <Panel title="Файли" description="Оберіть три файли для порівняння. Формат перевіряється під час завантаження.">
          <div className="grid gap-4 lg:grid-cols-3">
            <FileField
              label="Експорт із сайту"
              format="CSV"
              value={files.site}
              onPick={() => pick("site")}
              disabled={processing}
            />
            <FileField
              label="Експорт із 1С"
              format="XLS / XLSX"
              value={files.oneC}
              onPick={() => pick("oneC")}
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
                <Play className="size-4" /> Перевірити файли
              </Button>
            )}
          </div>
        </Panel>
      </TabsContent>

      <TabsContent value="settings" className="space-y-4"><fieldset disabled={processing} className="space-y-4">
        <Panel title="Правила оновлення" description="Визначають, який статус отримає товар після порівняння.">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <FieldRow label="Операція обробки">
              <Select value="update" onValueChange={() => {}}>
                <SelectTrigger className="h-9 text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="update">Оновлення статусів</SelectItem>
                </SelectContent>
              </Select>
            </FieldRow>
            <FieldRow label="Статус для товарів, яких немає в обох джерелах">
              <StatusSelect
                value={missingStatus}
                allowEmpty={false}
                onChange={(v) => {
                  setMissingStatus(v);
                  
                }}
              />
            </FieldRow>
            <FieldRow label="Який поточний статус замінювати у знайдених">
              <StatusSelect
                value={replaceStatus}
                allowEmpty={false}
                onChange={(v) => {
                  setReplaceStatus(v);
                  
                }}
              />
            </FieldRow>
            <div className="grid gap-3">
              <label className="flex items-start gap-2 text-xs">
                <Checkbox
                  checked={clearFound}
                  onCheckedChange={(v) => {
                    setClearFound(Boolean(v));
                    
                  }}
                />
                <span>Очищувати статус знайдених товарів</span>
              </label>
              <FieldRow label="Новий статус знайдених (якщо очищення вимкнено)">
                <StatusSelect
                  value={foundNewStatus}
                  allowEmpty
                  disabled={clearFound}
                  onChange={(v) => {
                    setFoundNewStatus(v);
                    
                  }}
                />
              </FieldRow>
            </div>
          </div>
          <p className="mt-4 text-[11px] leading-relaxed text-muted-foreground">
            Інші статуси знайдених товарів залишаються без змін. Товар вважається знайденим, якщо його артикул є
            щонайменше в одному джерелі — 1С або постачальник.
          </p>
        </Panel>

        <div className="grid gap-4 lg:grid-cols-2">
          <Panel title="Читання файлів">
            <div className="space-y-4">
              <div className="space-y-3 rounded-md border border-border bg-surface p-3">
                <p className="text-xs font-semibold">Експорт із сайту (CSV)</p>
                <label className="flex items-center gap-2 text-xs">
                  <Checkbox checked={csvHeaders} onCheckedChange={(v) => { setCsvHeaders(Boolean(v));  }} />
                  Перший рядок CSV містить заголовки
                </label>
                <div className="grid gap-3 sm:grid-cols-2">
                  <FieldRow label="Стовпець «Артикул»">
                    <Input className="h-9 text-xs" value={csvHeaders ? settings.rules.skuColumn : "1"} onChange={(e)=>change("rules","skuColumn",e.target.value)} disabled={!csvHeaders} />
                  </FieldRow>
                  <FieldRow label="Стовпець «Статус»">
                    <Input className="h-9 text-xs" value={csvHeaders ? settings.rules.statusColumn : "2"} onChange={(e)=>change("rules","statusColumn",e.target.value)} disabled={!csvHeaders} />
                  </FieldRow>
                  <FieldRow label="Роздільник">
                    <SimpleSelect
                      value={delimiter}
                      onChange={(v) => { setDelimiter(v);  }}
                      options={[
                        ["auto", "Авто"],
                        [";", "Крапка з комою"],
                        [",", "Кома"],
                        ["\t", "Табуляція"],
                      ]}
                    />
                  </FieldRow>
                  <FieldRow label="Кодування">
                    <SimpleSelect
                      value={encoding}
                      onChange={(v) => { setEncoding(v);  }}
                      options={[
                        ["auto", "Авто"],
                        ["utf-8", "UTF-8"],
                        ["windows-1251", "Windows-1251"],
                      ]}
                    />
                  </FieldRow>
                </div>
                {!csvHeaders && (
                  <p className="text-[11px] text-muted-foreground">
                    Без заголовків використовуються 1-й стовпець (артикул) і 2-й стовпець (статус).
                  </p>
                )}
              </div>

              {(["Експорт із 1С", "Постачальник"] as const).map((src) => (
                <div key={src} className="space-y-3 rounded-md border border-border bg-surface p-3">
                  <p className="text-xs font-semibold">{src} (Excel)</p>
                  <label className="flex items-center gap-2 text-xs">
                    <Checkbox checked={settings[src === "Експорт із 1С" ? "oneC" : "supplier"].hasHeader} onCheckedChange={(v)=>change(src === "Експорт із 1С" ? "oneC" : "supplier", "hasHeader",Boolean(v))} />
                    Перший рядок містить заголовки
                  </label>
                  <FieldRow label="Аркуш" hint="Порожньо — використовується перший аркуш. Артикул читається з першого стовпця.">
                    <Input className="h-9 text-xs" placeholder="Наприклад: Лист1" value={settings[src === "Експорт із 1С" ? "oneC" : "supplier"].sheetName} onChange={(e)=>change(src === "Експорт із 1С" ? "oneC" : "supplier", "sheetName",e.target.value)} />
                  </FieldRow>
                </div>
              ))}
            </div>
          </Panel>

          <Panel title="Файл результату">
            <div className="grid gap-3 sm:grid-cols-2">
              <FieldRow label="Формат">
                <SimpleSelect
                  value={outFormat}
                  onChange={setOutFormat}
                  options={[
                    ["csv", "CSV"],
                    ["xlsx", "XLSX"],
                  ]}
                />
              </FieldRow>
              <div className="flex items-end">
                <label className="flex items-center gap-2 pb-2 text-xs">
                  <Checkbox checked={outHeader} onCheckedChange={(v) => setOutHeader(Boolean(v))} />
                  Додати рядок заголовків
                </label>
              </div>
              <FieldRow label="Назва заголовка артикула">
                <Input className="h-9 text-xs" value={settings.export.skuHeader} onChange={(e)=>change("export","skuHeader",e.target.value)} disabled={!outHeader} />
              </FieldRow>
              <FieldRow label="Назва заголовка статусу">
                <Input className="h-9 text-xs" value={settings.export.statusHeader} onChange={(e)=>change("export","statusHeader",e.target.value)} disabled={!outHeader} />
              </FieldRow>
              <FieldRow label="Роздільник CSV">
                <SimpleSelect
                  value={settings.export.delimiter}
                  onChange={(v)=>change("export","delimiter",v)}
                  options={[
                    ["source", "Як у вихідному файлі"],
                    [";", "Крапка з комою"],
                    [",", "Кома"],
                    ["\t", "Табуляція"],
                  ]}
                />
              </FieldRow>
              <FieldRow label="Кодування">
                <SimpleSelect
                  value={settings.export.encodingName}
                  onChange={(v)=>change("export","encodingName",v)}
                  options={[
                    ["source", "Як у вихідному файлі"],
                    ["utf-8-bom", "UTF-8 з BOM"],
                    ["utf-8", "UTF-8"],
                    ["windows-1251", "Windows-1251"],
                  ]}
                />
              </FieldRow>
            </div>
            <p className="mt-3 text-[11px] leading-relaxed text-muted-foreground">
              Усі додаткові стовпці з початкового експорту сайту зберігаються у файлі результату без змін.
            </p>
          </Panel>
        </div>
      </fieldset></TabsContent>

      <TabsContent value="result" className="space-y-4">
        {!result ? (
          <Panel>
            <EmptyState
              title="Результату ще немає"
              hint="Оберіть файли у розділі «Файли» та натисніть «Перевірити файли»."
            />
          </Panel>
        ) : (
          <>
            {stale && (
              <StateBanner tone="warning">
                Налаштування змінено після перевірки — результат нижче показано за новими правилами лише попередньо.
                Запустіть перевірку повторно.
              </StateBanner>
            )}
            <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
              <StatBlock label="Перевірено" value={stats.checked} tone="neutral" active={filter === "all"} onClick={() => selectBlock("all")} />
              <StatBlock label="Стали недоступними" value={stats.unavailable} tone="danger" active={filter === "unavailable"} onClick={() => selectBlock("unavailable")} />
              <StatBlock label="Знову доступні" value={stats.again} tone="success" active={filter === "again"} onClick={() => selectBlock("again")} />
              <StatBlock label="Усього змін статусу" value={stats.changed} tone="primary" active={filter === "changed"} onClick={() => selectBlock("changed")} />
              <StatBlock label="Без змін" value={stats.unchanged} tone="muted" active={filter === "unchanged"} onClick={() => selectBlock("unchanged")} />
            </div>

            <Panel
              title="Таблиця результату"
              description={`Показано ${visible.length} з ${computed.length} рядків · унікальних артикулів: ${uniqueSku}`}
              actions={
                <Button variant="ghost" size="sm" onClick={resetFilters}>
                  <RotateCcw className="size-3.5" /> Скинути
                </Button>
              }
            >
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <SearchInput value={search} onChange={setSearch} placeholder="Пошук за артикулом або статусом…" />
                <SimpleSelect
                  value={filter}
                  onChange={(v) => setFilter(v as Filter)}
                  options={[
                    ["all", "Тип зміни: усі"],
                    ["unavailable", "Стали недоступними"],
                    ["again", "Знову доступні"],
                    ["changed", "Зі зміною статусу"],
                    ["unchanged", "Без змін"],
                  ]}
                />
                <SimpleSelect
                  value={fCurrent}
                  onChange={setFCurrent}
                  options={[[ALL, "Поточний статус: усі"], [NO_STATUS, NO_STATUS], ...STATUSES.map((s) => [s, s] as [string, string])]}
                />
                <SimpleSelect
                  value={fNew}
                  onChange={setFNew}
                  options={[[ALL, "Новий статус: усі"], [NO_STATUS, NO_STATUS], ...STATUSES.map((s) => [s, s] as [string, string])]}
                />
              </div>

              <div className="mt-4 overflow-hidden rounded-md border border-border">
                <div className="max-h-[max(120px,min(460px,calc(100dvh-540px)))] overflow-auto">
                  <table className="w-full text-xs">
                    <thead className="sticky top-0 z-10 bg-surface text-muted-foreground">
                      <tr className="[&>th]:px-3 [&>th]:py-2 [&>th]:text-left [&>th]:font-medium">
                        <th className="w-16">Рядок</th>
                        <th className="w-44">Артикул</th>
                        <th>Поточний статус</th>
                        <th className="w-56">Новий статус</th>
                        <th className="w-56">Причина</th>
                      </tr>
                    </thead>
                    <tbody>
                      {visible.length === 0 ? (
                        <tr>
                          <td colSpan={5}>
                            <EmptyState
                              title="Нічого не знайдено за вибраними умовами"
                              hint="Змініть пошуковий запит або натисніть «Скинути»."
                            />
                          </td>
                        </tr>
                      ) : (
                        visible.slice(page * 200, (page + 1) * 200).map((r) => (
                          <tr key={r.row} className="border-t border-border hover:bg-surface/70">
                            <td className="px-3 py-1.5 tabular-nums text-muted-foreground">{r.row}</td>
                            <td className="px-2 py-1.5">
                              <CopySku sku={r.sku} />
                            </td>
                            <td className="px-3 py-1.5">
                              {r.current || <span className="text-muted-foreground">{NO_STATUS}</span>}
                            </td>
                            <td className="px-2 py-1.5">
                              <StatusSelect
                                value={r.next}
                                allowEmpty
                                onChange={(v) => void command("status","edit",{row:r.row,value:v})}
                                highlighted={r.changed}
                              />
                            </td>
                            <td className="px-3 py-1.5 text-muted-foreground">
                              {r.reason}
                              
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
              <Pages count={visible.length} page={page} setPage={setPage} hint="Фільтри й пошук впливають лише на перегляд і не змінюють обсяг збереження." />
            </Panel>

            <Panel>
              <div className="flex flex-wrap items-center justify-between gap-3">
                <label className="flex items-center gap-2 text-xs">
                  <Checkbox checked={onlyChanged} onCheckedChange={(v) => setOnlyChanged(Boolean(v))} />
                  Зберегти лише змінені товари
                </label>
                <div className="flex items-center gap-3">
                  <span className="text-xs text-muted-foreground">
                    До файлу потрапить {exportCount.toLocaleString("uk-UA")} рядків
                  </span>
                  <Button onClick={() => setSaveOpen(true)} disabled={processing}>
                    <Save className="size-4" /> Зберегти результат…
                  </Button>
                </div>
              </div>
            </Panel>
          </>
        )}
      </TabsContent>

      <TabsContent value="warnings">
        <Panel title={`Попередження — ${warnings.length}`}>
          {warnings.length === 0 ? (
            <EmptyState title="Попереджень немає" hint="Тут з'являться повідомлення після перевірки файлів." />
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
        <Panel title="Як користуватися">
          <ol className="space-y-3">
            {[
              ["Завантажте файли", "Експорт із сайту (CSV), експорт із 1С та файл постачальника (XLS/XLSX)."],
              ["Перевірте", "Натисніть «Перевірити файли» — програма порівняє артикули та застосує правила."],
              ["Перегляньте фільтри", "Клацніть блок статистики або скористайтеся пошуком і фільтрами."],
              ["Скопіюйте артикул або змініть статус", "Клік по артикулу копіює його; новий статус можна змінити вручну."],
              ["Оберіть обсяг вивантаження", "Лише змінені товари чи повний файл."],
              ["Збережіть", "Натисніть «Зберегти результат…» і вкажіть місце для файлу."],
            ].map(([t, d], i) => (
              <li key={t} className="flex gap-3">
                <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-primary text-[11px] font-semibold text-primary-foreground">
                  {i + 1}
                </span>
                <div>
                  <p className="text-xs font-semibold">{t}</p>
                  <p className="text-xs text-muted-foreground">{d}</p>
                </div>
              </li>
            ))}
          </ol>
        </Panel>
      </TabsContent>

      <Dialog open={saveOpen} onOpenChange={setSaveOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Зберегти результат</DialogTitle>
            <DialogDescription>Оберіть назву файлу та папку призначення.</DialogDescription>
          </DialogHeader>
          <div className="grid gap-3">
            <FieldRow label="Назва файлу">
              <Input value={fileName} onChange={(e) => setFileName(e.target.value)} className="h-9 text-xs" />
            </FieldRow>
            <FieldRow label="Папка">
              <FolderField value={folder} onChange={setFolder} />
            </FieldRow>
            <StateBanner>
              Формат: {outFormat.toUpperCase()} · рядків: {exportCount.toLocaleString("uk-UA")}
            </StateBanner>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setSaveOpen(false)}>
              Скасувати
            </Button>
            <Button
              disabled={processing} onClick={async () => { const response=await saveFile("status","save",folder,fileName); if (!response?.failed && !response?.cancelled) {setSaveOpen(false);toast.success("Результат збережено");} }}
            >
              Зберегти
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Tabs>
  );
}

function StatusSelect({
  value,
  onChange,
  allowEmpty,
  disabled,
  highlighted,
}: {
  value: Status;
  onChange: (v: Status) => void;
  allowEmpty?: boolean;
  disabled?: boolean;
  highlighted?: boolean;
}) {
  return (
    <Select
      value={value === "" ? CLEAR : value}
      onValueChange={(v) => onChange(v === CLEAR ? "" : (v as Status))}
      disabled={disabled ?? false}
    >
      <SelectTrigger className={`h-8 w-full text-xs ${highlighted ? "border-primary/50 bg-accent/40" : ""}`}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {value && !STATUSES.includes(value) && <SelectItem value={value} disabled>{value} (з файлу)</SelectItem>}
        {allowEmpty && <SelectItem value={CLEAR}>{NO_STATUS} (очистити)</SelectItem>}
        {STATUSES.map((s) => (
          <SelectItem key={s} value={s}>
            {s}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

function SimpleSelect({
  value,
  onChange,
  options,
}: {
  value: string;
  onChange: (v: string) => void;
  options: [string, string][];
}) {
  return (
    <Select value={value} onValueChange={onChange}>
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
