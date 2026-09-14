import { HelpGuide } from "./shared";
import { useEffect, useRef, useState } from 'react';
import { command, useDesktop } from '@/lib/bridge';
import type { ImageOptions } from '@/lib/types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Panel, FieldRow, FolderField, EmptyState } from './shared';
import { Download, FolderOpen, Loader2, Save, RotateCcw } from 'lucide-react';
import { toast } from 'sonner';

const selectClass='h-9 min-w-0 w-full rounded-md border border-input bg-background px-2 text-xs';

function OptionsForm({value,onChange,disabled,framing=false}:{value:ImageOptions;onChange:(next:ImageOptions)=>void;disabled:boolean;framing?:boolean}) {
  const set=(key:keyof ImageOptions,v:any)=>onChange({...value,[key]:v});
  return <fieldset disabled={disabled} className={`grid grid-cols-2 gap-3 [&_label]:min-w-0 [&_input]:min-w-0 lg:grid-cols-3`}>
    <FieldRow label="Розмір квадрата, px"><Input type="number" min={128} max={4096} value={value.size} onChange={e=>set('size',Number(e.target.value))}/></FieldRow>
    <FieldRow label="Квадрат"><select className={selectClass} value={value.mode} onChange={e=>set('mode',e.target.value)}><option value="crop">Обрізати до квадрата</option><option value="fit">Вписати з полями</option></select></FieldRow>
    <FieldRow label="Формат"><select className={selectClass} value={value.format} onChange={e=>set('format',e.target.value)}><option value="jpg">JPG</option><option value="png">PNG</option><option value="webp">WebP</option></select></FieldRow>
    <FieldRow label={`Якість JPG / WebP: ${value.quality}%`}><input type="range" min={1} max={100} value={value.quality} onChange={e=>set('quality',Number(e.target.value))}/></FieldRow>
    <FieldRow label="Колір фону"><input className="h-9 w-full rounded border border-input" type="color" value={value.background} onChange={e=>set('background',e.target.value)}/></FieldRow>
    <label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={value.enhance} onChange={e=>set('enhance',e.target.checked)}/>AI-покращення ×4 перед підготовкою квадрата</label>
    {framing&&<>
      <FieldRow label="Поворот"><select className={selectClass} value={value.rotation} onChange={e=>set('rotation',Number(e.target.value))}>{[0,90,180,270].map(v=><option key={v} value={v}>{v}°</option>)}</select></FieldRow>
      <FieldRow label={`Масштаб обрізки: ${value.zoom.toFixed(2)}×`}><input disabled={disabled||value.mode!=='crop'} type="range" min={1} max={4} step={.01} value={value.zoom} onChange={e=>set('zoom',Number(e.target.value))}/></FieldRow>
      <div className="grid gap-2"><FieldRow label="Горизонталь"><input aria-label="Горизонталь обрізки" disabled={disabled||value.mode!=='crop'} type="range" min={0} max={1} step={.01} value={value.x} onChange={e=>set('x',Number(e.target.value))}/></FieldRow><FieldRow label="Вертикаль"><input disabled={disabled||value.mode!=='crop'} type="range" min={0} max={1} step={.01} value={value.y} onChange={e=>set('y',Number(e.target.value))}/></FieldRow></div>
    </>}
  </fieldset>;
}

function CropCanvas({source,options,onChange,disabled}:{source:string;options:ImageOptions;onChange:(next:ImageOptions)=>void;disabled:boolean}) {
  const canvas=useRef<HTMLCanvasElement>(null);
  const drag=useRef<{x:number;y:number;ox:number;oy:number}|null>(null);
  useEffect(()=>{
    if(!source)return;
    let cancelled=false;const image=new Image();image.onload=()=>{
      if(cancelled||!canvas.current)return;
      const rotated=document.createElement('canvas');
      const swapped=options.rotation%180!==0;rotated.width=swapped?image.height:image.width;rotated.height=swapped?image.width:image.height;
      const rc=rotated.getContext('2d')!;rc.translate(rotated.width/2,rotated.height/2);rc.rotate(options.rotation*Math.PI/180);rc.drawImage(image,-image.width/2,-image.height/2);
      const ctx=canvas.current.getContext('2d')!;const size=500;ctx.fillStyle=options.background;ctx.fillRect(0,0,size,size);
      if(options.mode==='crop') { const side=Math.max(1,Math.floor(Math.min(rotated.width,rotated.height)/options.zoom));ctx.drawImage(rotated,Math.round((rotated.width-side)*options.x),Math.round((rotated.height-side)*options.y),side,side,0,0,size,size); }
      else {const scale=Math.min(size/rotated.width,size/rotated.height);const w=rotated.width*scale,h=rotated.height*scale;ctx.drawImage(rotated,(size-w)/2,(size-h)/2,w,h);}
    };image.src=source;return()=>{cancelled=true;};
  },[source,options]);
  return <canvas ref={canvas} width={500} height={500} aria-label="Попередній перегляд обрізки" className="aspect-square w-full max-w-[220px] touch-none rounded border border-border bg-white" style={{cursor:options.mode==='crop'?'grab':'default'}}
    onPointerDown={e=>{if(disabled||options.mode!=='crop')return;e.currentTarget.setPointerCapture(e.pointerId);drag.current={x:e.clientX,y:e.clientY,ox:options.x,oy:options.y};}}
    onPointerMove={e=>{if(!drag.current)return;const d=drag.current,w=e.currentTarget.clientWidth;onChange({...options,x:Math.min(1,Math.max(0,d.ox-(e.clientX-d.x)/w)),y:Math.min(1,Math.max(0,d.oy-(e.clientY-d.y)/w))});}}
    onPointerUp={()=>{drag.current=null;}} onPointerCancel={()=>{drag.current=null;}}/>;
}

export function ImagesModule() {
  const desktop=useDesktop()!,state=desktop.images;
  const [tab,setTab]=useState('load');
  const [url,setUrl]=useState('');
  const [defaults,setDefaults]=useState<ImageOptions>(state.defaults);
  const [batch,setBatch]=useState<ImageOptions>(state.defaults);
  const [options,setOptions]=useState<ImageOptions>(state.defaults);
  const [active,setActive]=useState(''),[source,setSource]=useState(''),[preview,setPreview]=useState('');
  const [selected,setSelected]=useState<Set<string>>(new Set());
  const [pending,setPending]=useState(false),[folder,setFolder]=useState(desktop.documents);
  const [previewOptions,setPreviewOptions]=useState('');
  const item=state.items.find(x=>x.id===active);
  const busy=state.busy||pending;
  const draft=!!item&&JSON.stringify(options)!==JSON.stringify(item.options);
  const matchingPreview=preview&&previewOptions===JSON.stringify(options);
  useEffect(()=>{if(draft&&active)void command('images','draft',{id:active});},[draft,active]);
  async function exec(action:string,data:any={}) {setPending(true);try{return await command('images',action,data);}finally{setPending(false);}}
  async function open(id:string) {
    const entry=state.items.find(x=>x.id===id);if(!entry)return;
    if(draft){toast.info('Застосуйте налаштування поточної картинки або скиньте правки перед перемиканням.');return;}
    setActive(id);setOptions({...entry.options});setSource('');setPreview('');setPreviewOptions('');
    const response=await exec('source',{id});if(!response?.failed)setSource(response.source);
  }
  async function apply() {
    const response=await exec('preview',{id:active,options});
    if(!response?.failed){setPreview(response.preview);setPreviewOptions(JSON.stringify(options));toast.success('Попередній перегляд готовий');}
  }
  async function load(local:boolean) {
    if(local)await exec('pick',{options:batch});
    else {
      const found=await exec('discover',{url:url.trim()});if(found?.failed)return;
      await exec('loadUrls',{paths:found.urls,options:batch});
    }
    setTab('edit');
  }
  async function save() {
    if(draft){toast.info('Спочатку застосуйте налаштування поточної картинки.');return;}
    const response=await exec('export',{folder,ids:[...selected]});
    if(!response?.failed)toast.success(`Збережено зображень: ${response.paths.length}`);
  }
  const oldIds=useRef(new Set<string>());
  useEffect(()=>{
    const added=state.items.filter(x=>!oldIds.current.has(x.id));
    if(added.length)setSelected(previous=>new Set([...previous,...added.map(x=>x.id)]));
    oldIds.current=new Set(state.items.map(x=>x.id));
  },[state.items]);

  return <div className="space-y-3" data-testid="images-module">
    <div><h2 className="text-sm font-semibold">Зображення</h2><p className="text-xs text-muted-foreground">Завантаження, квадрат, обробка та локальне AI-покращення.</p></div>
    {busy&&<div className="flex items-center gap-2 text-xs"><Loader2 className="size-4 animate-spin"/>{state.message}<Button size="sm" variant="ghost" onClick={()=>void command('images','cancel')}>Скасувати</Button></div>}
    <Tabs value={tab} onValueChange={setTab}><TabsList><TabsTrigger value="load">Завантаження</TabsTrigger><TabsTrigger value="edit">Зображення ({state.items.length})</TabsTrigger><TabsTrigger value="settings">Налаштування</TabsTrigger><TabsTrigger value="warnings">Попередження ({state.warnings.length})</TabsTrigger><TabsTrigger value="help">Як користуватися</TabsTrigger></TabsList>
      <TabsContent value="load"><div className="space-y-3"><Panel title="Звідки завантажити"><FieldRow label="Сторінка товару Makita або пряме HTTPS-посилання на фото"><Input disabled={busy} value={url} onChange={e=>setUrl(e.target.value)} placeholder="https://www.makita.nl/artikel/…"/></FieldRow><p className="mt-2 text-xs text-muted-foreground">Для інших сайтів можна спробувати сторінку або вказати пряме посилання на JPG, PNG, WebP.</p></Panel><Panel title="Налаштування цього завантаження" actions={<Button variant="ghost" size="sm" disabled={busy} onClick={()=>setBatch({...state.defaults})}><RotateCcw className="size-3.5"/>Взяти загальні</Button>}><OptionsForm value={batch} onChange={setBatch} disabled={busy}/><p className="mt-3 text-xs text-muted-foreground">Застосуються лише до нових фотографій. Після завантаження кожну можна налаштувати окремо.</p><div className="mt-4 flex gap-2"><Button disabled={busy||!url.trim()} onClick={()=>void load(false)}><Download className="size-4"/>Завантажити за посиланням</Button><Button variant="secondary" disabled={busy} onClick={()=>void load(true)}><FolderOpen className="size-4"/>Додати файли</Button></div></Panel></div></TabsContent>
      <TabsContent value="edit"><div className="space-y-3">{!state.items.length?<Panel><EmptyState title="Додайте зображення" hint={state.message}/></Panel>:<>
        <Panel><div className="mb-3 flex items-center gap-3"><label className="flex items-center gap-2 text-xs"><input type="checkbox" disabled={busy} checked={state.items.length>0&&selected.size===state.items.length} onChange={e=>setSelected(e.target.checked?new Set(state.items.map(x=>x.id)):new Set())}/>Обрати всі</label><span className="text-xs text-muted-foreground">Натисніть фото для редагування. Обрано: {selected.size}</span></div><div className="flex max-h-44 gap-3 overflow-auto">{state.items.map(photo=><div key={photo.id} className={`w-28 shrink-0 rounded-md border p-2 ${active===photo.id?'border-primary ring-1 ring-primary':'border-border'}`}><button disabled={busy} onClick={()=>void open(photo.id)} className="block w-full"><img src={photo.thumbnail} alt={photo.name} className="h-12 w-full object-contain"/></button><label className="mt-1 flex items-center gap-1 text-[10px]"><input type="checkbox" disabled={busy} checked={selected.has(photo.id)} onChange={e=>setSelected(old=>{const next=new Set(old);e.target.checked?next.add(photo.id):next.delete(photo.id);return next;})}/><span className="truncate" title={photo.name}>{photo.name}</span></label><div className="text-[10px] text-muted-foreground">{photo.width} × {photo.height}{photo.saved?' · ✓':''}</div></div>)}</div></Panel>
        {item?<Panel title={item.name} actions={<Button size="sm" variant="ghost" disabled={busy} onClick={async()=>{await exec('remove',{id:active});setSelected(old=>{const next=new Set(old);next.delete(active);return next;});setActive('');}}>Прибрати</Button>}>
          <div className="grid gap-4 lg:grid-cols-[1fr_1fr_1.1fr]"><div><p className="mb-2 text-xs font-medium">До · {item.width} × {item.height}</p><div className="flex aspect-square max-w-[220px] items-center justify-center rounded border border-border bg-white">{source?<img src={source} className="max-h-full max-w-full object-contain" alt="Оригінал"/>:<Loader2 className="animate-spin"/>}</div></div><div><p className="mb-2 text-xs font-medium">{matchingPreview?'Після обробки':'Кадрування'} · {options.size} × {options.size}</p>{matchingPreview?<img src={preview} alt="Результат обробки" className="aspect-square w-full max-w-[220px] rounded border border-border object-contain"/>:<CropCanvas source={source} options={options} onChange={setOptions} disabled={busy}/>}<p className="mt-2 text-[11px] text-muted-foreground">Перетягуйте фото для кадрування або використовуйте повзунки. AI і стиснення видно після застосування.</p>{matchingPreview&&<Button size="sm" variant="ghost" onClick={()=>setPreviewOptions('')}>Редагувати кадрування</Button>}</div><div><OptionsForm value={options} onChange={setOptions} disabled={busy} framing/><div className="mt-3 flex flex-wrap gap-2"><Button size="sm" disabled={busy||!source} onClick={()=>void apply()}>Застосувати та переглянути</Button><Button size="sm" variant="ghost" disabled={busy} onClick={()=>setOptions({...item.options})}>Скинути правки</Button></div><p className="mt-2 text-[11px] text-muted-foreground">{draft?'Є незастосовані правки.':'Налаштування картинки збережені для експорту.'}</p></div></div>
        </Panel>:<Panel><EmptyState title="Натисніть фотографію, щоб відкрити редактор"/></Panel>}
        <Panel><div className="grid items-end gap-3 md:grid-cols-[1fr_auto]"><FieldRow label="Папка для збереження"><FolderField value={folder} onChange={setFolder}/></FieldRow><Button disabled={busy||!selected.size||draft} onClick={()=>void save()}><Save className="size-4"/>Зберегти обрані ({selected.size})</Button></div><p className="mt-2 text-[11px] text-muted-foreground">Назва береться з оригіналу. Якщо файл уже існує, додається номер. Оригінали не перезаписуються.</p></Panel>
      </>}</div></TabsContent>
      <TabsContent value="settings"><div className="space-y-3"><Panel title="Загальні налаштування зображень"><OptionsForm value={defaults} onChange={setDefaults} disabled={busy}/><p className="mt-3 text-xs text-muted-foreground">Ці значення зберігаються між запусками. Для поточного завантаження натисніть «Взяти загальні». Уже відкриті картинки не змінюються.</p><Button className="mt-3" size="sm" disabled={busy} onClick={async()=>{const response=await exec('defaults',{options:defaults});if(!response?.failed){setBatch({...defaults});toast.success('Загальні налаштування збережені');}}}>Зберегти налаштування</Button></Panel><Panel title="Локальне AI-покращення"><p className="text-xs">Real-ESRGAN працює на вашому комп’ютері без добових лімітів. Потрібна відеокарта з Vulkan. Компонент завантажується один раз (~45 МБ), окремо від програми.</p><p className="mt-2 text-xs text-muted-foreground">Фото до 1024 px по довгій стороні збільшується ×4, потім готується квадрат потрібного розміру. Перевіряйте написи й дрібні деталі у порівнянні «До / Після».</p><Button className="mt-3" size="sm" disabled={busy||state.engineReady} onClick={()=>void exec('installEngine')}>{state.engineReady?'AI-компонент встановлено':'Завантажити AI-компонент'}</Button><p className="mt-2 text-[11px] text-muted-foreground">Real-ESRGAN © Xintao Wang та автори, BSD-3-Clause. Ліцензія додається до програми.</p></Panel></div></TabsContent>
      <TabsContent value="warnings"><Panel title="Попередження"><div className="max-h-[60vh] overflow-auto text-xs">{state.warnings.length?state.warnings.map((w,i)=><p className="border-b border-border py-2" key={i}>{w}</p>):<EmptyState title="Попереджень немає"/>}</div></Panel></TabsContent>
      <TabsContent value="help">
        <HelpGuide steps={[["Задайте загальні налаштування","У налаштуваннях задайте стандартний розмір, квадрат, фон і формат. За замовчуванням — обрізка 1000 × 1000."],
["Завантажте фотографії","На вкладці завантаження вставте посилання Makita або додайте файли. Параметри цього завантаження можна змінити незалежно від загальних."],
["Налаштуйте картинку","Натисніть фотографію. Перетягуйте кадр, змінюйте масштаб, поворот або перемкніть на поля, щоб зберегти товар цілим."],
["Перегляньте обробку","Натисніть «Застосувати та переглянути», щоб побачити кінцеву обробку. Для AI спочатку один раз завантажте компонент у налаштуваннях."],
["Збережіть фотографії","Позначте потрібні фотографії, оберіть папку й натисніть «Зберегти обрані». Кожне фото збережеться зі своїми параметрами."],
["Завершіть роботу","Загальні налаштування залишаються після закриття програми; фотографії та їхні правки поточної сесії потрібно зберегти перед виходом."]]} />
      </TabsContent>
    </Tabs>
  </div>;
}
