import { defineConfig } from 'orval';

// Cliente TypeScript generado desde el OpenAPI que produce TG.Api al compilar (openapi.json en este paquete).
export default defineConfig({
  tortillas: {
    input: './openapi.json',
    output: {
      mode: 'tags-split',
      target: './src/generado',
      schemas: './src/generado/modelos',
      client: 'react-query',
      clean: true,
      prettier: false,
      override: {
        mutator: { path: './src/cliente.ts', name: 'clienteApi' },
        query: { useQuery: true, useMutation: true, signal: true },
      },
    },
  },
});
