// Measure actual laid-out text; hidden tabs are re-measured when they become visible.
export function observe(element, dotnet, lines = 7) {
    let disposed = false, previous;
    function measure() {
        if(disposed || !element.isConnected || element.clientWidth === 0 || element.classList.contains("expanded"))return;
        const height = parseFloat(getComputedStyle(element).lineHeight) * lines;
        const overflow = element.scrollHeight > height + 1;
        if(previous !== overflow){previous=overflow;dotnet.invokeMethodAsync('SetOverflow',overflow).catch(()=>{});}
    }
    const resize = new ResizeObserver(measure);resize.observe(element);measure();
    return {measure,dispose(){disposed=true;resize.disconnect();}};
}