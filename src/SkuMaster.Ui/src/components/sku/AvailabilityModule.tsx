import { HelpGuide } from "./shared";
import { useEffect, useMemo, useState, useDeferredValue } from 'react';
import { command, changeSetting, useDesktop } from '@/lib/bridge';
import { STATUSES, NO_STATUS } from '@/lib/sku-data';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { Panel, FileField, StatBlock, CopySku, Pages, StateBanner, FieldRow, FolderField, EmptyState } from './shared';
import { Loader2, Play, Save } from 'lucide-react';
import { toast } from 'sonner';

const selectClass = 'h-9 rounded-md border border-input bg-background px-2 text-xs';
function StatusSelect({value,onChange,disabled,label}:{value:string;onChange:(v:string)=>void;disabled?:boolean;label:string}) {
  return <select aria-label={label} className={selectClass} value={value} disabled={disabled} onChange={e=>onChange(e.target.value)}>
    <option value="">{NO_STATUS}</option>{!STATUSES.includes(value)&&value&&<option value={value}>{value}</option>}{STATUSES.map(s=><option key={s}>{s}</option>)}
  </select>;
}

export function AvailabilityModule() {
  const desktop=useDesktop()!;
  const state=desktop.availability, settings=state.settings;
  const [tab,setTab]=useState('files'), [allStatuses,setAllStatuses]=useState(true);
  const [statuses,setStatuses]=useState<string[]>(['Снят с производства','Скоро в продаже']);
  const [filter,setFilter]=useState('all'),[search,setSearch]=useState(''),[page,setPage]=useState(0);
  const query=useDeferredValue(search);
  const [selected,setSelected]=useState<Set<number>>(new Set());
  const [bulk,setBulk]=useState('Снят с производства');
  const [pending,setPending]=useState(false),[saveMode,setSaveMode]=useState<'save'|'saveReport'|null>(null);
  const [folder,setFolder]=useState(desktop.documents),[fileName,setFileName]=useState('perevirka-statusiv');
  const busy=state.busy||pending;
  const scope=useMemo(()=>state.rows.filter(r=>allStatuses||statuses.includes(r.current)),[state.rows,allStatuses,statuses]);
  const visible=useMemo(()=>scope.filter(r=>(filter==='all'||filter==='found'&&r.available||filter==='absent'&&!r.available||filter==='changed'&&r.changed)&&`${r.sku} ${r.current} ${r.next}`.toLocaleLowerCase().includes(query.toLocaleLowerCase())),[scope,filter,query]);
  const allVisible=visible.length>0&&visible.every(r=>selected.has(r.row));
  const statusChoices=useMemo(()=>Array.from(new Set([...STATUSES,'',...state.rows.map(r=>r.current)])),[state.rows]);
  useEffect(()=>{setPage(0);setSelected(new Set());},[filter,search,allStatuses,statuses,state.hasResult]);
  useEffect(()=>{if(page>0&&page*200>=visible.length)setPage(Math.max(0,Math.ceil(visible.length/200)-1));},[visible.length,page]);
  function change(section:string,key:string,value:any){void changeSetting('availability',section,key,value);}
  async function execute(action:string,data:any={}){setPending(true);try{return await command('availability',action,data);}finally{setPending(false);}}
  async function run(){const response=await execute('analyze');if(!response?.failed){setSelected(new Set());setTab('result');}}
  async function edit(rows:number[],value:string){const response=await execute('edit',{rows,value});if(!response?.failed)toast.success(`Статус призначено: ${rows.length}`);}
  function stat(value:string){setFilter(value);setSearch('');setTab('result');}
  async function save(){
    const rows=(saveMode==='saveReport'?visible:scope).map(r=>r.row);
    const response=await execute(saveMode!,{folder,fileName,rows});
    if(!response?.failed&&!response?.cancelled){setSaveMode(null);toast.success('Файл збережено');}
  }
  const numberField=(label:string,section:string,key:string,value:number)=><FieldRow label={label}><Input type="number" min={1} value={value} onChange={e=>{const n=Number(e.target.value);if(Number.isInteger(n)&&n>0)change(section,key,n);}} /></FieldRow>;

  return <div className="space-y-3" data-testid="availability-module">
    <div className="flex items-center justify-between gap-3"><div><h2 className="text-sm font-semibold">Перевірка наявності</h2><p className="text-xs text-muted-foreground">Знайдіть товари у постачальника та підготуйте зміни статусів для сайту.</p></div></div>
    {state.busy&&<StateBanner><span className="inline-flex items-center gap-2"><Loader2 className="size-3.5 animate-spin"/>Обробка файлів…</span><Button variant="ghost" size="sm" onClick={()=>void command('availability','cancel')}>Скасувати</Button></StateBanner>}
    <Tabs value={tab} onValueChange={setTab}>
      <TabsList><TabsTrigger value="files">Файли</TabsTrigger><TabsTrigger value="result">Результат</TabsTrigger><TabsTrigger value="settings">Налаштування</TabsTrigger><TabsTrigger value="warnings">Попередження ({state.warnings.length})</TabsTrigger><TabsTrigger value="help">Як користуватися</TabsTrigger></TabsList>
      <TabsContent value="files"><Panel><div className="grid gap-4 md:grid-cols-2"><FileField label="Експорт сайту" format="CSV" value={state.files.site} disabled={busy} onPick={()=>void execute('pick',{source:'site'})}/><FileField label="Залишки постачальника" format="XLS / XLSX" value={state.files.supplier} disabled={busy} onPick={()=>void execute('pick',{source:'supplier'})}/></div><p className="mt-3 text-xs text-muted-foreground">За замовчуванням без заголовків: сайт — SKU у колонці 1, статус у колонці 2; постачальник — SKU у колонці 1. Наявність визначається лише за збігом SKU.</p><div className="mt-4 flex items-center gap-3"><Button size="sm" disabled={busy||!state.files.site||!state.files.supplier} onClick={run}>{busy?<Loader2 className="size-3.5 animate-spin"/>:<Play className="size-3.5"/>}Перевірити файли</Button><p className="text-xs text-muted-foreground">{state.message}</p></div></Panel></TabsContent>
      <TabsContent value="result"><div className="space-y-3">
        {!state.hasResult?<Panel><EmptyState title="Спочатку перевірте файли" hint={state.message}/></Panel>:<>
          <Panel><label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={allStatuses} disabled={busy} onChange={e=>setAllStatuses(e.target.checked)}/>Усі товари з файлу</label>{!allStatuses&&<div className="mt-3 flex flex-wrap gap-x-4 gap-y-2">{statusChoices.map(s=><label key={s} className="flex items-center gap-1.5 text-xs"><input type="checkbox" disabled={busy} checked={statuses.includes(s)} onChange={e=>setStatuses(e.target.checked?[...statuses,s]:statuses.filter(x=>x!==s))}/>{s||NO_STATUS}</label>)}</div>}<p className="mt-2 text-[11px] text-muted-foreground">Обрані статуси сайту визначають товари для перегляду, правок і експорту.</p></Panel>
          <div className="grid grid-cols-4 gap-2"><StatBlock label="Перевірено" value={scope.length} active={filter==='all'} onClick={()=>stat('all')}/><StatBlock label="Є у постачальника" value={scope.filter(r=>r.available).length} tone="success" active={filter==='found'} onClick={()=>stat('found')}/><StatBlock label="Немає у постачальника" value={scope.filter(r=>!r.available).length} tone="danger" active={filter==='absent'} onClick={()=>stat('absent')}/><StatBlock label="Змінено статус" value={scope.filter(r=>r.changed).length} tone="primary" active={filter==='changed'} onClick={()=>stat('changed')}/></div>
          <Panel><div className="flex flex-wrap items-center gap-2"><Input className="h-9 min-w-40 flex-1" placeholder="Пошук SKU або статусу…" value={search} onChange={e=>setSearch(e.target.value)}/><span className="text-xs">Обрано: {selected.size}</span><StatusSelect label="Масовий статус" value={bulk} disabled={busy} onChange={setBulk}/><Button size="sm" disabled={busy||!selected.size} onClick={()=>void edit([...selected],bulk)}>Призначити обраним</Button><Button size="sm" variant="ghost" disabled={!selected.size} onClick={()=>setSelected(new Set())}>Зняти вибір</Button></div>
            <div className="mt-3 max-h-[max(180px,calc(100vh-580px))] overflow-auto rounded-md border border-border"><table className="w-full text-left text-xs"><thead className="sticky top-0 bg-muted"><tr><th className="p-2"><input aria-label="Вибрати всі відфільтровані товари" title="Усі відфільтровані товари на всіх сторінках" type="checkbox" disabled={busy} checked={allVisible} onChange={e=>setSelected(e.target.checked?new Set(visible.map(r=>r.row)):new Set())}/></th><th className="p-2">SKU</th><th className="p-2">Статус на сайті</th><th className="p-2">Наявність</th><th className="p-2">Новий статус</th></tr></thead><tbody>{visible.slice(page*200,page*200+200).map(r=><tr key={r.row} className="border-t border-border"><td className="p-2"><input aria-label={`Обрати ${r.sku}`} type="checkbox" disabled={busy} checked={selected.has(r.row)} onChange={e=>setSelected(old=>{const next=new Set(old);e.target.checked?next.add(r.row):next.delete(r.row);return next;})}/></td><td className="p-2"><CopySku sku={r.sku}/></td><td className="p-2">{r.current||NO_STATUS}</td><td className={`p-2 ${r.available?'text-success':'text-muted-foreground'}`}>{r.available?'Є у постачальника':'Немає у постачальника'}</td><td className="p-2"><div className="flex items-center gap-2"><StatusSelect label={`Новий статус ${r.sku}`} value={r.next} disabled={busy} onChange={v=>void edit([r.row],v)}/>{r.changed&&<span title="Статус змінено" className="text-primary">●</span>}</div></td></tr>)}</tbody></table>{!visible.length&&<EmptyState title="Немає товарів за вибраними умовами"/>}</div>
            <Pages count={visible.length} page={page} setPage={setPage} hint={`Показано ${visible.length}. Вибрати всі — усі відфільтровані товари на всіх сторінках.`}/>
          </Panel>
          <Panel><div className="flex flex-wrap items-center justify-between gap-3"><label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={settings.export.onlyChanged} disabled={busy} onChange={e=>change('export','onlyChanged',e.target.checked)}/>Зберегти лише змінені товари ({scope.filter(r=>r.changed).length})</label><div className="flex gap-2"><Button variant="secondary" size="sm" disabled={busy||!visible.length} onClick={()=>{setFileName('perevirka-naiavnosti');setSaveMode('saveReport');}}>Зберегти звіт</Button><Button size="sm" disabled={busy||!(settings.export.onlyChanged?scope.some(r=>r.changed):scope.length)} onClick={()=>{setFileName('zminy-statusiv');setSaveMode('save');}}><Save className="size-3.5"/>Зберегти зміни статусів</Button></div></div><p className="mt-2 text-[11px] text-muted-foreground">Зміни статусів: SKU та новий статус для обраних статусів сайту, незалежно від пошуку й наявності. Звіт: поточний відфільтрований список із наявністю.</p></Panel>
        </>}
      </div></TabsContent>
      <TabsContent value="settings"><fieldset disabled={busy} className="space-y-3"><Panel title="Вхідні файли"><div className="grid grid-cols-2 gap-4 md:grid-cols-3">{numberField('Сайт: колонка SKU','','skuColumn',settings.skuColumn)}{numberField('Сайт: колонка статусу','','statusColumn',settings.statusColumn)}<FieldRow label="Сайт: перший рядок"><select className={selectClass} value={String(settings.site.hasHeader)} onChange={e=>change('site','hasHeader',e.target.value==='true')}><option value="false">Одразу товар</option><option value="true">Заголовки</option></select></FieldRow><FieldRow label="Роздільник CSV"><select className={selectClass} value={settings.site.delimiter} onChange={e=>change('site','delimiter',e.target.value)}>{[['auto','Автоматично'],[';','Крапка з комою'],[',','Кома'],['\t','Табуляція']].map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></FieldRow><FieldRow label="Кодування CSV"><select className={selectClass} value={settings.site.encodingName} onChange={e=>change('site','encodingName',e.target.value)}>{['auto','utf-8','windows-1251'].map(v=><option key={v}>{v}</option>)}</select></FieldRow>{numberField('Постачальник: колонка SKU','supplier','skuColumn',settings.supplier.skuColumn)}{numberField('Постачальник: перший рядок даних','supplier','firstDataRow',settings.supplier.firstDataRow)}<FieldRow label="Аркуш постачальника (порожньо — перший)"><Input value={settings.supplier.sheetName} onChange={e=>change('supplier','sheetName',e.target.value)}/></FieldRow></div><p className="mt-3 text-xs text-muted-foreground">Зміна вхідних налаштувань скидає результат. Повторна перевірка також скидає ручні правки.</p></Panel><Panel title="Експорт"><div className="grid gap-4 md:grid-cols-3"><FieldRow label="Формат"><select className={selectClass} value={settings.export.format} onChange={e=>change('export','format',e.target.value)}><option value="csv">CSV</option><option value="xlsx">Excel (.xlsx)</option></select></FieldRow><FieldRow label="Заголовок SKU"><Input value={settings.export.skuHeader} onChange={e=>change('export','skuHeader',e.target.value)}/></FieldRow><FieldRow label="Заголовок статусу"><Input value={settings.export.statusHeader} onChange={e=>change('export','statusHeader',e.target.value)}/></FieldRow><label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={settings.export.includeHeaders} onChange={e=>change('export','includeHeaders',e.target.checked)}/>Додати заголовки</label></div><p className="mt-3 text-xs text-muted-foreground">CSV: UTF-8 із BOM, роздільник «;». SKU зберігається як текст.</p></Panel></fieldset></TabsContent>
      <TabsContent value="warnings"><Panel title="Попередження"><div className="max-h-[60vh] overflow-auto text-xs">{state.warnings.length?state.warnings.map((w,i)=><p className="border-b border-border py-2" key={i}>{w}</p>):<EmptyState title="Попереджень немає"/>}</div></Panel></TabsContent>
      <TabsContent value="help">
        <HelpGuide steps={[["Завантажте файли","Додайте CSV сайту та Excel залишків постачальника. За потреби вкажіть колонки й заголовки в налаштуваннях."],
["Перевірте наявність","Натисніть «Перевірити файли». Наявність — це збіг SKU, кількість не перевіряється."],
["Оберіть статуси","Залиште «Усі товари з файлу» для готової вибірки або зніміть галочку та оберіть потрібні статуси сайту."],
["Перегляньте результат","Натисніть «Є у постачальника» або «Немає у постачальника». Шукайте та копіюйте SKU натисканням."],
["Змініть статуси","Змініть «Новий статус» окремого товару або відмітьте товари, оберіть статус і натисніть «Призначити обраним». Загальна галочка обирає всі сторінки поточного фільтра."],
["Оберіть новий статус","Для зняття з виробництва оберіть «Снят с производства». Для товарів, що повернулися, можна поставити «Новинка» або «Без статусу»."],
["Збережіть результат","Натисніть «Зберегти зміни статусів», оберіть папку та ім’я. Файл містить SKU і новий статус; за замовчуванням лише зміни. «Зберегти звіт» зберігає відфільтрований список із наявністю."]]} />
      </TabsContent>
    </Tabs>
    <Dialog open={!!saveMode} onOpenChange={open=>{if(!open&&!busy)setSaveMode(null);}}><DialogContent><DialogHeader><DialogTitle>{saveMode==='saveReport'?'Зберегти звіт':'Зберегти зміни статусів'}</DialogTitle><DialogDescription>Формат: {settings.export.format.toUpperCase()}. Оберіть папку та назву файлу.</DialogDescription></DialogHeader><FieldRow label="Папка"><FolderField value={folder} onChange={setFolder}/></FieldRow><FieldRow label="Назва файлу"><Input value={fileName} onChange={e=>setFileName(e.target.value)}/></FieldRow><DialogFooter><Button variant="secondary" disabled={busy} onClick={()=>setSaveMode(null)}>Скасувати</Button><Button disabled={busy} onClick={()=>void save()}>{busy&&<Loader2 className="size-3.5 animate-spin"/>}Зберегти</Button></DialogFooter></DialogContent></Dialog>
  </div>;
}
