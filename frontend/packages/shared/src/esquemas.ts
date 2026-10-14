import { z } from 'zod';

/** Validaciones compartidas por la web y la app de ruta (mismas reglas en ambos lados). */

export const textoRequerido = (mensaje = 'Este dato es obligatorio.') => z.string().trim().min(1, mensaje);

/** Cantidad positiva con máximo `decimales` decimales (RN-CAT-06). */
export const cantidad = (decimales = 4, mensaje = 'Captura una cantidad mayor que cero.') =>
  z.coerce
    .number({ message: 'Captura un número.' })
    .positive(mensaje)
    .refine((v) => Number(v.toFixed(decimales)) === v, {
      message: decimales === 0 ? 'La cantidad debe ser entera.' : `Máximo ${decimales} decimales.`,
    });

export const importe = z.coerce.number({ message: 'Captura un importe.' }).min(0, 'El importe no puede ser negativo.');

export const tasa = z.coerce
  .number({ message: 'Captura la tasa.' })
  .min(0, 'La tasa va de 0 a 100 %.')
  .max(1, 'La tasa va de 0 a 100 %.');

/** RFC con formato válido: 12 (moral) o 13 (física) caracteres (RN-FAC-03). */
export const rfc = z
  .string()
  .trim()
  .toUpperCase()
  .regex(/^([A-ZÑ&]{3,4})(\d{2})(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])([A-Z\d]{2})([A\d])$/, 'El RFC no tiene un formato válido.');

export const codigoPostal = z.string().trim().regex(/^\d{5}$/, 'El código postal tiene 5 dígitos.');

export const correo = z.string().trim().email('El correo no es válido.');

export const uuid = z.string().uuid('Elige una opción.');
