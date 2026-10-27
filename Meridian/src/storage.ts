import type { MeridianProject, StoredModule } from './core/types.ts';
import type { LibraryCanvasDocument, LibraryCatalogItem, StoredCanvasLibrary } from './core/library.ts';
export interface ProjectSummary { id: string; title: string; revision: number; updatedAt: string }
export interface Recovery { project: MeridianProject; baseRevision: number; savedAt: string }
export interface LibraryRecovery extends Recovery { library: LibraryCanvasDocument }
export interface Checkpoint { id: string; name: string; createdAt: string; revision: number }
export class StorageError extends Error { constructor(message: string, public status: number) { super(message); } }
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  if (!response.ok) {
    const data = await response.json().catch(() => ({ error: response.statusText }));
    throw new StorageError(data.error ?? data.message ?? `Request failed (${response.status})`, response.status);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}
const json = (method: string, value: unknown): RequestInit => ({ method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value) });
/** Home persists a normal canvas graph, with separate saved-workspace launch locations. */
export const libraryStorage = {
  load: () => request<StoredCanvasLibrary>('/api/library'),
  catalog: () => request<LibraryCatalogItem[]>('/api/library/catalog'),
  save: (library: LibraryCanvasDocument, expectedRevision: number) => request<LibraryCanvasDocument>('/api/library', json('PUT', { library, expectedRevision })),
  recover: async (): Promise<LibraryRecovery | null> => { try { return await request('/api/library/recovery'); } catch (error) { if (error instanceof StorageError && error.status === 404) return null; throw error; } },
  autosave: (library: LibraryCanvasDocument, baseRevision: number, expectedSavedAt: string | null) => request<LibraryRecovery>('/api/library/recovery', json('PUT', { library, baseRevision, expectedSavedAt })),
  clearRecovery: (recovery: Recovery) => request<void>('/api/library/recovery', json('DELETE', { baseRevision: recovery.baseRevision, savedAt: recovery.savedAt })),
  checkpoints: () => request<Checkpoint[]>('/api/library/checkpoints'),
  checkpoint: (library: LibraryCanvasDocument, name: string) => request<Checkpoint>('/api/library/checkpoints', json('POST', { library, name })),
  loadCheckpoint: (id: string) => request<LibraryCanvasDocument>(`/api/library/checkpoints/${encodeURIComponent(id)}`),
};
export interface ModuleSummary { id: string; name: string; createdAt: string; rootId: string; nodeCount: number; linkCount: number; contents: { kind: string; count: number }[] }
export const moduleStorage = {
  list: () => request<ModuleSummary[]>('/api/modules'),
  load: (id: string) => request<StoredModule>(`/api/modules/${encodeURIComponent(id)}`),
  save: (module: StoredModule) => request<StoredModule>('/api/modules', json('POST', { module })),
};
export interface ProjectStorage {
  list(): Promise<ProjectSummary[]>;
  load(id: string): Promise<MeridianProject>;
  create(project: MeridianProject): Promise<MeridianProject>;
  save(project: MeridianProject, expectedRevision: number): Promise<MeridianProject>;
  recover(id: string): Promise<Recovery | null>;
  autosave(project: MeridianProject, baseRevision: number, expectedSavedAt: string | null): Promise<Recovery>;
  clearRecovery(id: string, recovery: Recovery): Promise<void>;
  checkpoints(id: string): Promise<Checkpoint[]>;
  checkpoint(project: MeridianProject, name: string): Promise<Checkpoint>;
  loadCheckpoint(id: string, checkpointId: string): Promise<MeridianProject>;
  importFile(file: File): Promise<MeridianProject>;
  exportFile(project: MeridianProject): Promise<Blob>;
}
export const storage: ProjectStorage = {
  list: () => request('/api/projects'),
  load: id => request(`/api/projects/${encodeURIComponent(id)}`),
  create: p => request('/api/projects', json('POST', p)),
  save: (p, r) => request(`/api/projects/${encodeURIComponent(p.id)}`, json('PUT', { project:p, expectedRevision:r })),
  recover: async id => { try { return await request(`/api/projects/${encodeURIComponent(id)}/recovery`); } catch (e) { if (e instanceof StorageError && e.status === 404) return null; throw e; } },
  autosave: (p,r,expectedSavedAt) => request(`/api/projects/${encodeURIComponent(p.id)}/recovery`, json('PUT',{project:p,baseRevision:r,expectedSavedAt})),
  clearRecovery: (id,recovery) => request(`/api/projects/${encodeURIComponent(id)}/recovery`, json('DELETE',{baseRevision:recovery.baseRevision,savedAt:recovery.savedAt})),
  checkpoints: id => request(`/api/projects/${encodeURIComponent(id)}/checkpoints`),
  checkpoint: (p,name) => request(`/api/projects/${encodeURIComponent(p.id)}/checkpoints`,json('POST',{name,project:p})),
  loadCheckpoint: (id,cp) => request(`/api/projects/${encodeURIComponent(id)}/checkpoints/${encodeURIComponent(cp)}`),
  importFile: async file => { const body = new FormData(); body.append('file',file); return request('/api/import',{method:'POST',body}); },
  exportFile: async p => { const response = await fetch('/api/export',json('POST',p)); if (!response.ok) { const data = await response.json().catch(()=>({error:'Project export failed'})); throw new StorageError(data.error,response.status); } return response.blob(); }
};
