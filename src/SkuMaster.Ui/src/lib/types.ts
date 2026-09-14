export interface StatusSettings {
  schemaVersion: number;
  operationId: string;
  rules: { skuColumn: string; statusColumn: string; unavailableStatus: string; matchStatus: string; clearFoundStatus: boolean; replacementStatus: string };
  csvInput: { hasHeader: boolean; delimiter: string; encodingName: string };
  oneC: { hasHeader: boolean; sheetName: string };
  supplier: { hasHeader: boolean; sheetName: string };
  export: { onlyChanged: boolean; format: string; includeHeaders: boolean; skuHeader: string; statusHeader: string; delimiter: string; encodingName: string; skuColumn: string; statusColumn: string };
}
export interface MissingFileOptions { skuColumn: number; nameColumn: number; firstDataRow: number; sheetName: string; delimiter: string; encodingName: string }
export interface MissingSettings { site: MissingFileOptions; supplier: MissingFileOptions; exclusions: MissingFileOptions; outputFormat: string }
export interface StatusRow { row: number; sku: string; current: string; next: string; reason: string; isManual: boolean; changed: boolean; becameUnavailable: boolean; againAvailable: boolean }
export interface MissingRow { sku: string; name: string; excluded: boolean; onSite: boolean }
interface ModuleState { busy: boolean; hasResult: boolean; canSave: boolean; unsaved: boolean; message: string; warnings: string[] }
export interface DesktopSnapshot {
  images: { defaults: ImageOptions; busy: boolean; message: string; engineReady: boolean; warnings: string[]; items: ImageItem[] };
  availability: ModuleState & {
    files: { site: string; supplier: string };
    settings: { site: StatusSettings['csvInput']; skuColumn: number; statusColumn: number; supplier: MissingFileOptions; export: StatusSettings['export'] };
    rows: { row: number; sku: string; current: string; next: string; available: boolean; changed: boolean }[];
  };
  version: string;
  documents: string;
  status: ModuleState & {
    files: { site: string; oneC: string; supplier: string };
    settings: StatusSettings;
    rows: StatusRow[];
    stats: { checked: number; unavailable: number; again: number; changed: number; unchanged: number };
    siteColumns: string[]; oneCSheets: string[]; supplierSheets: string[];
  };
  missing: ModuleState & {
    files: { site: string; supplier: string; exceptions: string };
    settings: MissingSettings;
    rows: MissingRow[];
    exceptions: string[];
    canAnalyze: boolean; canExportExclusions: boolean;
    stats: { all: number; onsite: number; excluded: number; addable: number };
  };
}
export interface ImageOptions { size:number;mode:'crop'|'fit';format:'jpg'|'png'|'webp';quality:number;background:string;x:number;y:number;zoom:number;rotation:number;enhance:boolean }
export interface ImageItem { id:string;name:string;width:number;height:number;thumbnail:string;options:ImageOptions;saved:boolean }
