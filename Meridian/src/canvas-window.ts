/** A window reserved during the user's gesture, before an asynchronous save. */
export interface CanvasWindow {
  open: (projectId: string, canvasId?: string) => void;
  openLibrary: (canvasId: string) => void;
  cancel: () => void;
}

export function reserveCanvasWindow(): CanvasWindow {
  const waiting = new URL('/', window.location.origin);
  waiting.searchParams.set('window', 'waiting');
  // Request a desktop window rather than a browser tab. The local app host
  // supplies a managed child WebView; browsers honor this as a popup request.
  const child = window.open(waiting.href, '_blank', 'popup,width=1280,height=900');
  if (!child) throw new Error('Allow Meridian to open windows, then open this Work again.');
  let opened = false;
  return {
    open(projectId, canvasId) {
      if (child.closed) throw new Error('The Work window was closed before it finished opening. Open the saved Work from your library.');
      const destination = new URL('/', window.location.origin);
      destination.searchParams.set('project', projectId);
      if (canvasId) destination.searchParams.set('canvas', canvasId);
      child.location.replace(destination.href);
      opened = true;
      child.focus();
    },
    openLibrary(canvasId) {
      if(child.closed)throw new Error('The Work window was closed before it finished opening.');
      const destination=new URL('/',window.location.origin);destination.searchParams.set('libraryCanvas',canvasId);
      child.location.replace(destination.href);opened=true;child.focus();
    },
    cancel() { if (!opened && !child.closed) child.close(); },
  };
}
