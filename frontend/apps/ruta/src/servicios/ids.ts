import * as Crypto from 'expo-crypto';
import { uuidv7 } from '@/dominio/identificadores';

/** UUID v7 con aleatoriedad criptográfica del teléfono. */
export function nuevoId(): string {
  return uuidv7((n) => Crypto.getRandomBytes(n));
}
