import { describe, expect, it } from 'vitest';
import { ENTIDADES, resumenImportacion } from './CargaInicial';

describe('Carga inicial', () => {
  it('resume la vista previa sin errores', () => {
    expect(
      resumenImportacion({ entidad: 'productos', validarSolo: true, aplicado: false, filas: 3, creados: 2, actualizados: 1, errores: [] }),
    ).toBe('3 filas: Se crearían 2 y se actualizarían 1.');
  });

  it('con errores avisa que no se importó nada', () => {
    expect(
      resumenImportacion({
        entidad: 'productos',
        validarSolo: false,
        aplicado: false,
        filas: 2,
        creados: 0,
        actualizados: 0,
        errores: [{ fila: 2, campo: 'sku', mensaje: 'Obligatorio' }],
      }),
    ).toBe('1 problema en 2 filas; no se importó nada.');
  });

  it('cubre las cinco entidades de T-7.3', () => {
    expect(ENTIDADES.map((e) => e.entidad)).toEqual(['productos', 'proveedores', 'clientes', 'recetas', 'existencias']);
  });
});
