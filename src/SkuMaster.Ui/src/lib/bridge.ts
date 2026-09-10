import { useSyncExternalStore } from 'react';
import { toast } from 'sonner';
import type { DesktopSnapshot } from './types';

declare global { interface Window { chrome?: { webview?: { postMessage(value: unknown): void; addEventListener(type: string, listener: (event: any) => void): void } } } }
let snapshot: DesktopSnapshot | null = null;
let sequence = 0;
const subscribers = new Set<() => void>();
const requests = new Map<number, { resolve: (value: any) => void; reject: (error: Error) => void }>();
const host = window.chrome?.webview;
const pendingSettings: Record<string, number> = { status: 0, missing: 0 };
let serverSnapshot: DesktopSnapshot | null = null;
host?.addEventListener('message', ({ data }) => {
  if (data.snapshot) {
    serverSnapshot = data.snapshot;
    const next = { ...serverSnapshot };
    for (const module of ['status','missing']) {
      if (pendingSettings[module] && snapshot) next[module] = { ...next[module], settings: snapshot[module].settings };
    }
    snapshot = next; subscribers.forEach(fn => fn());
  }
  if (data.event) window.dispatchEvent(new CustomEvent('desktop-event', { detail: data }));
  if (data.id) {
    const request = requests.get(data.id);
    requests.delete(data.id);
    if (data.error) request?.reject(new Error(data.error)); else request?.resolve(data.value);
  }
});
export function useDesktop(): DesktopSnapshot | null { return useSyncExternalStore(fn => { subscribers.add(fn); return () => subscribers.delete(fn); }, () => snapshot); }
export function rawCommand(module: string, action: string, data: any = {}): Promise<any> {
  if (!host) return Promise.reject(new Error('Відкрийте встановлену програму SKU Майстер.'));
  const id = ++sequence;
  return new Promise((resolve, reject) => { requests.set(id, { resolve, reject }); host.postMessage({ id, module, action, data }); });
}
export async function command(module: string, action: string, data: any = {}): Promise<any> {
  try { return await rawCommand(module, action, data); }
  catch (error) { toast.error(error instanceof Error ? error.message : String(error)); return { failed: true }; }
}
export async function saveFile(module: string, action: string, folder: string, fileName: string) {
  return command(module, action, { folder, fileName });
}
export async function changeSetting(module: string, section: string, key: string, value: any) {
  const current = snapshot[module].settings;
  const settings = section ? { ...current, [section]: { ...current[section], [key]: value } } : { ...current, [key]: value };
  pendingSettings[module]++;
  snapshot = { ...snapshot, [module]: { ...snapshot[module], settings } };
  subscribers.forEach(fn => fn());
  try { await command(module, 'setting', { section, key, value }); }
  finally {
    pendingSettings[module]--;
    if (!pendingSettings[module] && serverSnapshot) {
      snapshot = { ...snapshot, [module]: { ...snapshot[module], settings: serverSnapshot[module].settings } };
      subscribers.forEach(fn => fn());
    }
  }
}
host && void command('app', 'ready');
