import { cn } from '../utils';

/**
 * Logotipo provisional de Tortillas Guerreras (pila de tortillas con espiga de maíz). Se reemplaza por el del cliente
 * cuando lo entregue; va sobre una placa clara como en la barra superior del SGO.
 */
export function Logo({ className, titulo = 'Tortillas Guerreras' }: { className?: string; titulo?: string }) {
  return (
    <svg viewBox="0 0 64 48" role="img" aria-label={titulo} className={cn('h-8 w-auto', className)}>
      <title>{titulo}</title>
      <ellipse cx="30" cy="38" rx="24" ry="7" fill="#d9a441" />
      <ellipse cx="30" cy="34" rx="24" ry="7" fill="#e8b85a" />
      <ellipse cx="30" cy="30" rx="24" ry="7" fill="#f2c96d" />
      <ellipse cx="30" cy="30" rx="24" ry="7" fill="none" stroke="#b07a1f" strokeWidth="1.2" />
      <circle cx="22" cy="29" r="1.3" fill="#c48a2a" />
      <circle cx="35" cy="31.5" r="1.1" fill="#c48a2a" />
      <circle cx="41" cy="28" r="0.9" fill="#c48a2a" />
      <path d="M50 26 C52 16 56 10 60 6" stroke="#2f7d32" strokeWidth="2.4" fill="none" strokeLinecap="round" />
      <ellipse cx="56" cy="13" rx="3.4" ry="7" transform="rotate(35 56 13)" fill="#f5c542" stroke="#c99a1a" strokeWidth="0.8" />
      <path d="M52 22 C49 17 47 13 47 8" stroke="#3f9a43" strokeWidth="2" fill="none" strokeLinecap="round" />
    </svg>
  );
}
