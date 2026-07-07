// Blazor에서 생성한 파일을 브라우저 다운로드로 전달 (CSV 내보내기 등)
window.amesDownload = function (fileName, base64, mimeType) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType || 'application/octet-stream' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
};
