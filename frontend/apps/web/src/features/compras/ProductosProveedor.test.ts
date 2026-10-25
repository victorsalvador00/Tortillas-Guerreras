import { describe, expect, it } from 'vitest';
import { validarPrecioPactado } from './ProductosProveedor';

describe('productos del proveedor', () => {
  it('RN-COM-10: el precio pactado es opcional y no negativo', () => {
    expect(validarPrecioPactado('')).toBeNull();
    expect(validarPrecioPactado('  ')).toBeNull();
    expect(validarPrecioPactado('6500.50')).toBeNull();
    expect(validarPrecioPactado('-1')).not.toBeNull();
    expect(validarPrecioPactado('abc')).not.toBeNull();
  });
});
