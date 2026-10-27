namespace Hypershell.Services;

/// <summary>Preview-only pointer movement. The native builder owns every committed model change.</summary>
public static class HyperlibHeaderPreviewDragSource
{
    public const string Script = """
        (()=>{
          if(window.top!==window||window.hyperlibGuidedHeaderDrag)return;
          const containerSelector='.studio-header-row-content,.studio-header-box-content';
          const controls='button,input,select,textarea,a[href],video,audio,iframe,canvas,svg,[contenteditable="true"],[role="button"],[role="slider"],[data-action]';
          let capability=null,greatestGeneration=-1,sequence=0,gesture=null,overlay=null,overlayElement=null;
          function send(message){try{window.chrome?.webview?.postMessage(message);}catch{}}
          function regionOf(element){return element.closest('[data-studio-region]')?.dataset.studioRegion
            ||element.closest('[data-studio-layout-region]')?.dataset.studioLayoutRegion||'header';}
          function previousSize(element){
            const d=element.dataset,values={x:Number(d.headerX),y:Number(d.headerY),widthMode:d.headerWidthMode,
              widthPercent:Number(d.headerWidthPercent),widthPixels:Number(d.headerWidthPixels),height:Number(d.headerHeight),positionAlignment:d.headerAlignment};
            return ['pixels','percent'].includes(values.widthMode)&&['manual','left','center','right'].includes(values.positionAlignment)
              &&['x','y','widthPercent','widthPixels','height'].every(key=>Number.isFinite(values[key]))?values:null;
          }
          function showOverlay(element){
            overlayElement=element;
            if(!capability||!element?.isConnected||!element.hasAttribute('data-preview-header-resizable')){if(overlay)overlay.hidden=true;return;}
            if(!overlay){
              overlay=document.createElement('div');overlay.id='studio-header-preview-resize';overlay.setAttribute('aria-hidden','true');
              for(const handle of ['n','ne','e','se','s','sw','w','nw']){const node=document.createElement('div');node.dataset.headerResizeHandle=handle;overlay.append(node);}
              document.body.append(overlay);
            }
            const b=element.getBoundingClientRect();overlay.hidden=false;overlay.style.left=b.left+'px';overlay.style.top=b.top+'px';
            overlay.style.width=b.width+'px';overlay.style.height=b.height+'px';
          }
          function markMovable(){
            document.querySelectorAll('[data-header-element]').forEach(element=>{
              const parent=element.parentElement;
              const editable=!!capability&&regionOf(element)===capability.region&&parent?.matches(containerSelector);
              element.toggleAttribute('data-preview-header-draggable',editable&&parent.dataset.headerArrangement==='positioned');
              element.toggleAttribute('data-preview-header-resizable',editable&&!!previousSize(element));
            });
            if(capability&&!document.getElementById('studio-header-preview-drag-style')){
              const style=document.createElement('style');style.id='studio-header-preview-drag-style';
              style.textContent='[data-preview-header-draggable]::after{content:"⠿";position:absolute;top:3px;right:3px;width:18px;height:18px;line-height:16px;text-align:center;font:16px system-ui;background:#15272de6;color:#e1ece9;border:1px solid #82b5aa;border-radius:3px;z-index:20;opacity:0;cursor:grab;pointer-events:auto;box-sizing:border-box}[data-preview-header-draggable]:hover::after{opacity:1}'
                +'#studio-header-preview-resize{position:fixed;z-index:2147483000;border:1px solid #86b9ad;box-sizing:border-box;pointer-events:none}#studio-header-preview-resize[hidden]{display:none}'
                +'#studio-header-preview-resize>[data-header-resize-handle]{position:absolute;width:9px;height:9px;background:#c3dfd6;border:1px solid #355e55;box-sizing:border-box;pointer-events:auto;transform:translate(-50%,-50%)}'
                +'[data-header-resize-handle=n]{left:50%;top:0;cursor:ns-resize}[data-header-resize-handle=ne]{left:100%;top:0;cursor:nesw-resize}[data-header-resize-handle=e]{left:100%;top:50%;cursor:ew-resize}[data-header-resize-handle=se]{left:100%;top:100%;cursor:nwse-resize}[data-header-resize-handle=s]{left:50%;top:100%;cursor:ns-resize}[data-header-resize-handle=sw]{left:0;top:100%;cursor:nesw-resize}[data-header-resize-handle=w]{left:0;top:50%;cursor:ew-resize}[data-header-resize-handle=nw]{left:0;top:0;cursor:nwse-resize}';
              document.head.append(style);
            }
            showOverlay(overlayElement);
          }
          function restore(g){
            for(const [key,value,priority] of g.styles){
              if(value)g.element.style.setProperty(key,value,priority);else g.element.style.removeProperty(key);
            }
          }
          function finish(rollback){
            const g=gesture;if(!g)return;gesture=null;
            if(rollback)restore(g);
            document.documentElement.style.userSelect=g.userSelect;
            document.documentElement.style.cursor=g.cursor;
            if(g.element.hasPointerCapture?.(g.pointerId))g.element.releasePointerCapture(g.pointerId);
            showOverlay(g.element);
          }
          function cancel(){finish(true);}
          function area(parent){
            const bounds=parent.getBoundingClientRect(),style=getComputedStyle(parent);
            const scaleX=parent.offsetWidth?bounds.width/parent.offsetWidth:1;
            const scaleY=parent.offsetHeight?bounds.height/parent.offsetHeight:1;
            return {left:bounds.left+parent.clientLeft*scaleX,top:bounds.top+parent.clientTop*scaleY,
              width:parent.clientWidth,height:parent.clientHeight,scaleX,scaleY,
              padLeft:parseFloat(style.paddingLeft)||0,padRight:parseFloat(style.paddingRight)||0,
              padTop:parseFloat(style.paddingTop)||0,padBottom:parseFloat(style.paddingBottom)||0};
          }
          function eligible(event){
            if(!capability||event.button!==0||event.isPrimary===false||event.ctrlKey||event.metaKey||event.altKey||event.shiftKey)return null;
            const target=event.target instanceof Element?event.target:null;if(!target||target.closest(controls))return null;
            // A real library tool owns its entire interactive surface, including labels and output.
            const template=target.closest('[data-studio-shared-template]');
            if(template&&template.querySelector(controls))return null;
            const element=target.closest('[data-header-element]');if(!element)return null;
            const parent=element.parentElement?.closest(containerSelector);
            if(!parent||parent.dataset.headerArrangement!=='positioned'||element.parentElement!==parent||regionOf(element)!==capability.region)return null;
            const owner=parent.classList.contains('studio-header-row-content')?parent.closest('[data-header-row]'):parent.closest('[data-header-element]');
            const parentId=owner?.dataset.headerRow||owner?.dataset.headerElement;
            const previousX=Number(element.dataset.headerX),previousY=Number(element.dataset.headerY);
            if(!parentId||!element.dataset.headerElement||!Number.isFinite(previousX)||!Number.isFinite(previousY)
              ||!element.hasAttribute('data-header-x')||!element.hasAttribute('data-header-y'))return null;
            const region=area(parent);if(region.width<=0||region.height<=0||region.scaleX<=0||region.scaleY<=0)return null;
            return {element,parent,parentId,previousX,previousY};
          }
          document.addEventListener('pointerdown',event=>{
            if(gesture)return;
            const handle=event.target instanceof Element?event.target.closest('[data-header-resize-handle]')?.dataset.headerResizeHandle:null;
            let candidate;
            if(handle&&capability&&overlayElement?.isConnected&&regionOf(overlayElement)===capability.region&&event.button===0){
              const element=overlayElement,parent=element.parentElement,previous=previousSize(element);
              if(!parent?.matches(containerSelector)||!previous)return;
              const owner=parent.classList.contains('studio-header-row-content')?parent.closest('[data-header-row]'):parent.closest('[data-header-element]');
              candidate={element,parent,parentId:owner?.dataset.headerRow||owner?.dataset.headerElement,previousX:previous.x,previousY:previous.y,previous,handle};
              if(!candidate.parentId)return;
            }else candidate=eligible(event);
            if(!candidate)return;
            const bounds=candidate.element.getBoundingClientRect();
            const parentArea=area(candidate.parent);
            const ownStyle=getComputedStyle(candidate.element),number=name=>parseFloat(ownStyle[name])||0;
            gesture={...candidate,capability:{...capability},pointerId:event.pointerId,startX:event.clientX,startY:event.clientY,
              grabX:event.clientX-bounds.left,grabY:event.clientY-bounds.top,active:false,x:0,y:0,
              initial:{left:(bounds.left-parentArea.left)/parentArea.scaleX,top:(bounds.top-parentArea.top)/parentArea.scaleY,width:bounds.width/parentArea.scaleX,height:bounds.height/parentArea.scaleY},
              minimumWidth:Math.max(16,number('paddingLeft')+number('paddingRight')+number('borderLeftWidth')+number('borderRightWidth')),
              minimumHeight:Math.max(16,number('paddingTop')+number('paddingBottom')+number('borderTopWidth')+number('borderBottomWidth')),
              styles:['left','top','right','transform','width','height','max-width','flex'].map(key=>[key,candidate.element.style.getPropertyValue(key),candidate.element.style.getPropertyPriority(key)]),
              userSelect:document.documentElement.style.userSelect,cursor:document.documentElement.style.cursor};
            document.documentElement.style.userSelect='none';
            try{candidate.element.setPointerCapture?.(event.pointerId);}catch{cancel();}
            if(handle){event.preventDefault();event.stopPropagation();}
          },true);
          document.addEventListener('pointermove',event=>{
            const g=gesture;
            if(!g){
              if(event.target instanceof Element&&!event.target.closest('[data-header-resize-handle]')){
                const hovered=event.target.closest('[data-preview-header-resizable]');
                const current=overlayElement?.isConnected?overlayElement.getBoundingClientRect():null;
                const near=current&&event.clientX>=current.left-7&&event.clientX<=current.right+7&&event.clientY>=current.top-7&&event.clientY<=current.bottom+7;
                const outside=current&&(event.clientX<current.left||event.clientX>current.right||event.clientY<current.top||event.clientY>current.bottom);
                showOverlay(near&&outside?overlayElement:hovered||near&&overlayElement||null);
              }return;
            }
            if(event.pointerId!==g.pointerId)return;
            if(!capability||g.capability.token!==capability.token||!g.element.isConnected||!g.parent.isConnected||g.element.parentElement!==g.parent
              ||regionOf(g.element)!==capability.region||!g.handle&&g.parent.dataset.headerArrangement!=='positioned'){cancel();return;}
            if(!g.active&&Math.hypot(event.clientX-g.startX,event.clientY-g.startY)<3)return;
            g.active=true;document.documentElement.style.cursor=g.handle?getComputedStyle(overlay.querySelector('[data-header-resize-handle="'+g.handle+'"]')).cursor:'grabbing';
            const region=area(g.parent),bounds=g.element.getBoundingClientRect();
            if(region.width<=0||region.height<=0||region.scaleX<=0||region.scaleY<=0){cancel();return;}
            if(g.handle){
              const horizontal=/[ew]/.test(g.handle),vertical=/[ns]/.test(g.handle),free=g.parent.dataset.headerArrangement==='positioned';
              const dx=(event.clientX-g.startX)/region.scaleX,dy=(event.clientY-g.startY)/region.scaleY;
              let left=g.initial.left,top=g.initial.top,right=left+g.initial.width,bottom=top+g.initial.height;
              const minWidth=Math.max(g.minimumWidth,g.previous.widthMode==='percent'?region.width*.01:0),minHeight=g.minimumHeight;
              if(horizontal&&minWidth>region.width-region.padLeft-region.padRight||free&&vertical&&minHeight>region.height-region.padTop-region.padBottom){cancel();return;}
              if(free){
                if(g.handle.includes('w'))left=Math.max(region.padLeft,g.previous.widthMode==='pixels'?right-4000:0,Math.min(right-minWidth,left+dx));
                if(g.handle.includes('e'))right=Math.min(region.width-region.padRight,g.previous.widthMode==='pixels'?left+4000:16000,Math.max(left+minWidth,right+dx));
                if(g.handle.includes('n'))top=Math.max(region.padTop,bottom-4000,Math.min(bottom-minHeight,top+dy));
                if(g.handle.includes('s'))bottom=Math.min(region.height-region.padBottom,top+4000,Math.max(top+minHeight,bottom+dy));
              }else{
                right=left+(horizontal?Math.min(region.width-region.padLeft-region.padRight,g.previous.widthMode==='pixels'?4000:16000,Math.max(minWidth,g.initial.width+(g.handle.includes('w')?-dx:dx))):g.initial.width);
                bottom=top+(vertical?Math.min(4000,Math.max(minHeight,g.initial.height+(g.handle.includes('n')?-dy:dy))):g.initial.height);
              }
              g.width=right-left;g.height=bottom-top;g.parentWidth=region.width;g.parentHeight=region.height;
              g.x=free?left/region.width*100:g.previous.x;g.y=free?top/region.height*100:g.previous.y;
              if(horizontal){
                const percent=g.width/region.width*100;
                g.element.style.width=g.previous.widthMode==='pixels'?g.width+'px':percent+'%';
                g.element.style.maxWidth=g.parent.dataset.headerArrangement==='horizontal'&&g.previous.widthMode==='percent'?percent+'%':'100%';
                if(!free&&g.parent.dataset.headerArrangement==='horizontal')g.element.style.flex=g.previous.widthMode==='pixels'?'0 0 '+g.width+'px':percent+' 1 0px';
              }
              if(vertical)g.element.style.height=g.height+'px';
              if(free){g.element.style.left=g.x+'%';g.element.style.top=g.y+'%';g.element.style.right='auto';g.element.style.transform='none';}
              showOverlay(g.element);event.preventDefault();event.stopPropagation();return;
            }
            const width=bounds.width/region.scaleX,height=bounds.height/region.scaleY;
            const left=Math.max(region.padLeft,Math.min(Math.max(region.padLeft,region.width-region.padRight-width),
              (event.clientX-region.left-g.grabX)/region.scaleX));
            const top=Math.max(region.padTop,Math.min(Math.max(region.padTop,region.height-region.padBottom-height),
              (event.clientY-region.top-g.grabY)/region.scaleY));
            g.x=Math.max(0,Math.min(100,left/region.width*100));g.y=Math.max(0,Math.min(100,top/region.height*100));
            g.element.style.left=g.x+'%';g.element.style.top=g.y+'%';g.element.style.right='auto';g.element.style.transform='none';
            event.preventDefault();event.stopPropagation();
          },true);
          document.addEventListener('pointerup',event=>{
            const g=gesture;if(!g||event.pointerId!==g.pointerId)return;
            if(!g.active){finish(true);return;}
            event.preventDefault();event.stopPropagation();
            if(!capability||g.capability.token!==capability.token||!g.element.isConnected||g.element.parentElement!==g.parent
              ||!g.handle&&g.parent.dataset.headerArrangement!=='positioned'){cancel();return;}
            const message=g.handle?{type:'studio-header-resize',...g.capability,elementId:g.element.dataset.headerElement,parentId:g.parentId,handle:g.handle,
              x:g.x,y:g.y,width:g.width,height:g.height,parentWidth:g.parentWidth,parentHeight:g.parentHeight,
              previous:{...g.previous,renderedWidth:g.initial.width,renderedHeight:g.initial.height},sequence:++sequence}
              :{type:'studio-header-drag',...g.capability,elementId:g.element.dataset.headerElement,parentId:g.parentId,
                x:g.x,y:g.y,previousX:g.previousX,previousY:g.previousY,sequence:++sequence};
            finish(false);capability=null;markMovable();send(message);
          },true);
          document.addEventListener('pointercancel',event=>{if(gesture?.pointerId===event.pointerId)cancel();},true);
          document.addEventListener('lostpointercapture',event=>{if(gesture?.pointerId===event.pointerId)cancel();},true);
          document.addEventListener('keydown',event=>{if(gesture&&event.key==='Escape'){cancel();event.preventDefault();event.stopPropagation();}},true);
          document.addEventListener('visibilitychange',()=>{if(document.hidden)cancel();});
          window.addEventListener('blur',cancel);
          document.addEventListener('scroll',()=>showOverlay(overlayElement),true);
          window.addEventListener('resize',()=>showOverlay(overlayElement));
          document.addEventListener('dragstart',event=>{if(event.target instanceof Element&&event.target.closest('[data-preview-header-draggable]'))event.preventDefault();},true);
          window.hyperlibGuidedHeaderDrag={
            configure(config){
              if(!config){cancel();capability=null;markMovable();return false;}
              if(typeof config.token!=='string'||!config.token||typeof config.documentId!=='string'||!config.documentId
                ||!Number.isSafeInteger(config.generation)||config.generation<greatestGeneration)return false;
              cancel();capability=null;
              if(config.region!==undefined&&!['header','navigation','body','footer'].includes(config.region))return false;
              greatestGeneration=config.generation;capability={token:config.token,generation:config.generation,documentId:config.documentId,region:config.region||'header'};
              markMovable();
              send({type:'studio-header-drag-ready',...capability});return true;
            },cancel
          };
        })();
        """;
}
