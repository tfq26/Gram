/**
 * Gram Client-Side Media Compressor
 * Compresses images and converts to WebP in browser before uploading to save 80-90% bandwidth & storage.
 */
window.gramMedia = {
    compressImage: async function(base64Data, contentType, maxDimension = 1920, quality = 0.82) {
        return new Promise((resolve, reject) => {
            const img = new Image();
            img.onload = () => {
                let width = img.naturalWidth || img.width;
                let height = img.naturalHeight || img.height;

                if (width > maxDimension || height > maxDimension) {
                    if (width > height) {
                        height = Math.round((height * maxDimension) / width);
                        width = maxDimension;
                    } else {
                        width = Math.round((width * maxDimension) / height);
                        height = maxDimension;
                    }
                }

                const canvas = document.createElement('canvas');
                canvas.width = width;
                canvas.height = height;
                const ctx = canvas.getContext('2d');
                ctx.drawImage(img, 0, 0, width, height);

                const outputFormat = 'image/webp';
                const compressedDataUrl = canvas.toDataURL(outputFormat, quality);
                
                resolve({
                    dataUrl: compressedDataUrl,
                    base64: compressedDataUrl.split(',')[1],
                    contentType: outputFormat,
                    width: width,
                    height: height
                });
            };
            img.onerror = (err) => reject(err);
            img.src = base64Data.startsWith('data:') ? base64Data : `data:${contentType};base64,${base64Data}`;
        });
    }
};
