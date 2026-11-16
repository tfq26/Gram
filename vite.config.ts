import { defineConfig } from 'vite';
import path from 'path';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src/ui'),
    },
  },
  server: {
    host: '127.0.0.1',
    port: 5174,
    strictPort: true,
    proxy: {
      '/api': 'http://127.0.0.1:7337',
      '/icons': 'http://127.0.0.1:7337',
      '/logo': 'http://127.0.0.1:7337',
      '/shadcn.css': 'http://127.0.0.1:7337',
      '/app.css': 'http://127.0.0.1:7337',
      '/v2-adapter.css': 'http://127.0.0.1:7337',
      '/audio.js': 'http://127.0.0.1:7337',
      '/settings': 'http://127.0.0.1:7337',
    },
  },
  build: {
    outDir: 'dist/studio',
    emptyOutDir: true,
  },
});
