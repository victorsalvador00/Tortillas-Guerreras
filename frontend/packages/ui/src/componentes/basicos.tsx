import { cva, type VariantProps } from 'class-variance-authority';
import { forwardRef, type ButtonHTMLAttributes, type HTMLAttributes, type ReactNode } from 'react';
import { cn } from '../utils';

/**
 * Íconos propios del negocio que no existen en Material Symbols. Mismo lienzo de 24 × 24, trazo de 2 px y
 * `currentColor`, para que se comporten como los de la fuente (tamaño, color y estado seleccionado).
 */
const ICONOS_PROPIOS: Record<string, ReactNode> = {
  // Mazorca de maíz inclinada: granos en cuadrícula y hojas que la abrazan desde la base.
  maiz: (
    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="none" stroke="currentColor" strokeWidth={1.6}
      strokeLinecap="round" strokeLinejoin="round" style={{ display: 'block' }}>
      <g transform="rotate(35 12 12)">
        <ellipse cx="12" cy="9" rx="3.9" ry="7.4" fill="currentColor" fillOpacity={0.18} />
        <path d="M10.2 2.6v12.6M13.8 2.6v12.6M8.3 5.5h7.4M8.1 8.6h7.8M8.2 11.7h7.6" strokeWidth={1.1} />
        <path d="M12 21.6C7.3 20.2 5.9 15.3 7.3 8.8c1.1 3.9 2.6 6.9 4.7 8.6" fill="currentColor" fillOpacity={0.45} />
        <path d="M12 21.6c4.7-1.4 6.1-6.3 4.7-12.8-1.1 3.9-2.6 6.9-4.7 8.6" fill="currentColor" fillOpacity={0.45} />
      </g>
    </svg>
  ),
};

/**
 * Ícono de Material Symbols (`<Icono nombre="inventory_2" />`) o propio (`<Icono nombre="maiz" />`, ver
 * `ICONOS_PROPIOS`). Decorativo salvo que tenga `etiqueta`.
 */
export function Icono({
  nombre,
  className,
  etiqueta,
  tamano,
}: {
  nombre: string;
  className?: string;
  etiqueta?: string;
  tamano?: number;
}) {
  const propio = ICONOS_PROPIOS[nombre];
  return (
    <span
      className={cn('icono', className)}
      aria-hidden={etiqueta ? undefined : true}
      aria-label={etiqueta}
      role={etiqueta ? 'img' : undefined}
      style={tamano ? { fontSize: `calc(${tamano}px * var(--tg-escala-texto))` } : undefined}
    >
      {propio ?? nombre}
    </span>
  );
}

const estilosBoton = cva(
  'inline-flex items-center justify-center gap-2 whitespace-nowrap font-medium transition-[background,box-shadow,color] disabled:cursor-not-allowed disabled:opacity-50 select-none',
  {
    variants: {
      variante: {
        // Botón principal con sombra del color primario.
        primario: 'bg-primary text-on-primary shadow-primary hover:brightness-110 disabled:shadow-none',
        // Secundario en píldora suave sin contorno.
        secundario: 'bg-soft text-primary hover:brightness-95 dark:hover:brightness-125',
        texto: 'bg-transparent text-primary hover:bg-soft',
        peligro: 'bg-[var(--tg-status-red-fg)] text-white shadow-[0_4px_12px_rgb(183_28_28/30%)] hover:brightness-110',
        contorno: 'border border-outline-variant bg-transparent text-on-surface hover:bg-soft',
      },
      tamano: {
        normal: 'h-11 rounded-full px-6 text-sm',
        chico: 'h-9 rounded-full px-4 text-13',
        grande: 'h-14 rounded-full px-8 text-base',
      },
      bloque: { true: 'w-full' },
    },
    defaultVariants: { variante: 'primario', tamano: 'normal' },
  },
);

export interface BotonProps extends ButtonHTMLAttributes<HTMLButtonElement>, VariantProps<typeof estilosBoton> {
  icono?: string;
  iconoFinal?: string;
  cargando?: boolean;
}

export const Boton = forwardRef<HTMLButtonElement, BotonProps>(function Boton(
  { className, variante, tamano, bloque, icono, iconoFinal, cargando, children, disabled, type = 'button', ...props },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      className={cn(estilosBoton({ variante, tamano, bloque }), className)}
      disabled={disabled || cargando}
      aria-busy={cargando || undefined}
      {...props}
    >
      {cargando ? <Girador /> : icono ? <Icono nombre={icono} tamano={20} /> : null}
      {children}
      {iconoFinal && !cargando ? <Icono nombre={iconoFinal} tamano={20} /> : null}
    </button>
  );
});

/** Botón de ícono en círculo suave (editar, quitar, expandir). */
export const BotonIcono = forwardRef<
  HTMLButtonElement,
  ButtonHTMLAttributes<HTMLButtonElement> & { icono: string; etiqueta: string; plano?: boolean; tamano?: 'normal' | 'chico' }
>(function BotonIcono({ icono, etiqueta, plano, tamano = 'normal', className, type = 'button', ...props }, ref) {
  return (
    <button
      ref={ref}
      type={type}
      aria-label={etiqueta}
      title={etiqueta}
      className={cn(
        'inline-grid shrink-0 place-items-center rounded-full text-current transition-colors disabled:cursor-not-allowed disabled:opacity-40',
        tamano === 'chico' ? 'size-9' : 'size-10',
        plano ? 'hover:bg-[rgb(127_127_127/12%)]' : 'bg-soft text-primary hover:brightness-95',
        className,
      )}
      {...props}
    >
      <Icono nombre={icono} tamano={tamano === 'chico' ? 20 : 22} />
    </button>
  );
});

export function Girador({ className, etiqueta }: { className?: string; etiqueta?: string }) {
  return (
    <span
      role={etiqueta ? 'status' : undefined}
      aria-label={etiqueta}
      className={cn('inline-block size-5 animate-spin rounded-full border-2 border-current border-r-transparent', className)}
    />
  );
}

/** Tarjeta blanca con sombra suave (en oscuro, contorno). */
export function Tarjeta({
  className,
  children,
  titulo,
  subtitulo,
  acciones,
  ...props
}: HTMLAttributes<HTMLElement> & { titulo?: ReactNode; subtitulo?: ReactNode; acciones?: ReactNode }) {
  return (
    <section
      className={cn('rounded-card border border-card-border bg-card p-4 shadow-card sm:p-5', className)}
      {...props}
    >
      {(titulo || acciones) && (
        <header className="mb-3 flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            {titulo && <h2 className="m-0 text-base font-medium">{titulo}</h2>}
            {subtitulo && <p className="m-0 mt-0.5 text-13 text-muted">{subtitulo}</p>}
          </div>
          {acciones && <div className="flex flex-wrap gap-2">{acciones}</div>}
        </header>
      )}
      {children}
    </section>
  );
}

export type ColorEstado = 'gris' | 'azul' | 'amarillo' | 'verde' | 'naranja' | 'rojo';

const COLORES_ESTADO: Record<string, ColorEstado> = {
  Borrador: 'gris',
  Capturado: 'gris',
  Planeada: 'gris',
  Preparado: 'gris',
  Pendiente: 'gris',
  PorAprobar: 'amarillo',
  Abierto: 'azul',
  Abierta: 'azul',
  Autorizada: 'azul',
  Confirmado: 'azul',
  Liberada: 'azul',
  EnProceso: 'azul',
  EnRuta: 'azul',
  Asignado: 'azul',
  EnviadaProveedor: 'azul',
  Recibida: 'verde',
  RecibidaParcial: 'amarillo',
  EntregadoParcial: 'amarillo',
  Registrada: 'verde',
  Registrado: 'verde',
  Vigente: 'verde',
  Terminada: 'verde',
  Entregado: 'verde',
  Liquidado: 'verde',
  Cerrada: 'verde',
  Cerrado: 'verde',
  Timbrada: 'verde',
  Aplicada: 'verde',
  Activo: 'verde',
  Revisada: 'verde',
  Obsoleta: 'naranja',
  Error: 'rojo',
  Rechazada: 'rojo',
  Cancelada: 'rojo',
  Cancelado: 'rojo',
  Inactivo: 'rojo',
};

const ETIQUETAS_ESTADO: Record<string, string> = {
  PorAprobar: 'Por aprobar',
  EnProceso: 'En proceso',
  EnRuta: 'En ruta',
  RecibidaParcial: 'Recibida parcial',
  EntregadoParcial: 'Entregado parcial',
  EnviadaProveedor: 'Enviada al proveedor',
};

const CLASES_COLOR: Record<ColorEstado, string> = {
  gris: 'bg-[var(--tg-status-gray-bg)] text-[var(--tg-status-gray-fg)]',
  azul: 'bg-[var(--tg-status-blue-bg)] text-[var(--tg-status-blue-fg)]',
  amarillo: 'bg-[var(--tg-status-yellow-bg)] text-[var(--tg-status-yellow-fg)]',
  verde: 'bg-[var(--tg-status-green-bg)] text-[var(--tg-status-green-fg)]',
  naranja: 'bg-[var(--tg-status-orange-bg)] text-[var(--tg-status-orange-fg)]',
  rojo: 'bg-[var(--tg-status-red-bg)] text-[var(--tg-status-red-fg)]',
};

/** Separa PascalCase en palabras: "DiferenciaLiquidacion" → "Diferencia liquidacion". */
export function etiquetaEnum(valor: string): string {
  if (ETIQUETAS_ESTADO[valor]) return ETIQUETAS_ESTADO[valor];
  const texto = valor.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

/** Etiqueta de estado en píldora: mismo color para el mismo estado en cualquier documento. */
export function EtiquetaEstado({ estado, etiqueta, color }: { estado?: string; etiqueta?: string; color?: ColorEstado }) {
  const c = color ?? (estado ? (COLORES_ESTADO[estado] ?? 'gris') : 'gris');
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-3.5 py-1 text-13 font-medium',
        CLASES_COLOR[c],
      )}
    >
      {c === 'verde' && (
        <span aria-hidden className="mt-[-3px] ml-px h-2.5 w-[5px] rotate-45 border-r-2 border-b-2 border-current" />
      )}
      {etiqueta ?? (estado ? etiquetaEnum(estado) : '')}
    </span>
  );
}

/** Aviso informativo, de advertencia o de error. */
export function Aviso({
  tipo = 'info',
  children,
  className,
  titulo,
}: {
  tipo?: 'info' | 'advertencia' | 'error' | 'exito';
  children: ReactNode;
  className?: string;
  titulo?: string;
}) {
  const clases = {
    info: CLASES_COLOR.azul,
    advertencia: CLASES_COLOR.amarillo,
    error: CLASES_COLOR.rojo,
    exito: CLASES_COLOR.verde,
  }[tipo];
  const icono = { info: 'info', advertencia: 'warning', error: 'error', exito: 'check_circle' }[tipo];
  return (
    <div
      role={tipo === 'error' ? 'alert' : 'status'}
      className={cn('flex items-start gap-3 rounded-lg px-4 py-3 text-sm', clases, className)}
    >
      <Icono nombre={icono} tamano={20} className="mt-px" />
      <div className="min-w-0">
        {titulo && <p className="m-0 font-medium">{titulo}</p>}
        <div>{children}</div>
      </div>
    </div>
  );
}

export function EstadoVacio({
  icono = 'inbox',
  titulo,
  mensaje,
  accion,
}: {
  icono?: string;
  titulo?: string;
  mensaje: string;
  accion?: ReactNode;
}) {
  return (
    <div role="status" className="flex flex-col items-center gap-2 px-4 py-10 text-center text-muted">
      <Icono nombre={icono} tamano={44} />
      {titulo && <h2 className="m-0 text-lg font-medium text-on-surface">{titulo}</h2>}
      <p className="m-0 max-w-[48ch]">{mensaje}</p>
      {accion}
    </div>
  );
}

/** Barra de acción fija abajo en pantallas de captura: resumen y acción principal a la mano. */
export function BarraAccion({ children, resumen, className }: { children: ReactNode; resumen?: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        'sticky bottom-3 z-10 mt-4 flex flex-wrap items-center justify-between gap-3 rounded-card border border-card-border bg-card px-4 py-3 shadow-card',
        className,
      )}
    >
      <div className="min-w-0 text-sm">{resumen}</div>
      <div className="flex flex-wrap items-center gap-2">{children}</div>
    </div>
  );
}

/** Indicador del tablero: ícono arriba a la izquierda, número grande a la derecha, título y nota. */
export function TarjetaIndicador({
  icono,
  valor,
  titulo,
  nota,
  tono,
  onClick,
}: {
  icono: string;
  valor: ReactNode;
  titulo: string;
  nota?: string;
  tono?: 'normal' | 'alerta' | 'bien';
  onClick?: () => void;
}) {
  const Componente = onClick ? 'button' : 'div';
  return (
    <Componente
      type={onClick ? 'button' : undefined}
      onClick={onClick}
      className={cn(
        'flex min-h-32 flex-col rounded-card border border-card-border bg-card p-4 text-left shadow-card transition-colors',
        onClick && 'cursor-pointer hover:bg-row-hover',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <Icono
          nombre={icono}
          className={cn(tono === 'alerta' ? 'text-[var(--tg-status-red-fg)]' : tono === 'bien' ? 'text-[var(--tg-status-green-fg)]' : 'text-on-surface')}
        />
        <span className="text-28 leading-none font-normal tabular-nums">{valor}</span>
      </div>
      <span className="mt-4 text-15 font-medium">{titulo}</span>
      {nota && <span className="mt-1 text-13 text-muted">{nota}</span>}
    </Componente>
  );
}

/** Par etiqueta/valor para fichas de detalle. */
export function Dato({ etiqueta, children, className }: { etiqueta: string; children: ReactNode; className?: string }) {
  return (
    <div className={cn('min-w-0', className)}>
      <dt className="text-xs text-muted">{etiqueta}</dt>
      <dd className="m-0 mt-0.5 font-medium break-words">{children ?? '—'}</dd>
    </div>
  );
}

export function Datos({ children, className }: { children: ReactNode; className?: string }) {
  return <dl className={cn('m-0 grid grid-cols-2 gap-x-6 gap-y-4 sm:grid-cols-3 lg:grid-cols-4', className)}>{children}</dl>;
}
