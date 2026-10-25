import * as Popover from '@radix-ui/react-popover';
import {
  forwardRef,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react';
import { cn } from '../utils';
import { Girador, Icono } from './basicos';
import { EntradaFecha } from './campo-fecha';

interface PropsBaseCampo {
  etiqueta: string;
  error?: string;
  ayuda?: ReactNode;
  className?: string;
  /** Sin etiqueta flotante y menos alto: para tablas de captura. */
  compacto?: boolean;
}

function PieCampo({ id, error, ayuda }: { id: string; error?: string; ayuda?: ReactNode }) {
  if (error) {
    return (
      <p id={`${id}-error`} className="m-0 mt-1 px-5 text-xs text-error">
        {error}
      </p>
    );
  }
  if (ayuda) {
    return (
      <p id={`${id}-ayuda`} className="m-0 mt-1 px-5 text-xs text-muted">
        {ayuda}
      </p>
    );
  }
  return null;
}

export type CampoProps = PropsBaseCampo &
  Omit<InputHTMLAttributes<HTMLInputElement>, 'className'> & {
    sufijo?: ReactNode;
    prefijo?: string;
  };

/** Campo de texto en píldora con etiqueta flotante (estilo "outlined"). Compatible con `register()` de RHF. */
export const Campo = forwardRef<HTMLInputElement, CampoProps>(function Campo(
  { etiqueta, error, ayuda, className, compacto, sufijo, prefijo, id, ...props },
  ref,
) {
  const generado = useId();
  const campoId = id ?? generado;
  if (props.type === 'date') {
    // Fechas siempre en DD/MM/AAAA (EntradaFecha), sin importar el idioma del navegador.
    const { type: _tipo, ...resto } = props;
    return (
      <div className={cn('min-w-0', className)}>
        <div className={cn('tg-campo', compacto && 'tg-campo-compacto')} data-lleno="true">
          <EntradaFecha
            ref={ref}
            idVisible={campoId}
            claseVisible="tg-control pr-14 tabular-nums"
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? `${campoId}-error` : ayuda ? `${campoId}-ayuda` : undefined}
            aria-label={compacto ? etiqueta : undefined}
            {...resto}
          />
          <label htmlFor={campoId}>{etiqueta}</label>
        </div>
        {!compacto && <PieCampo id={campoId} error={error} ayuda={ayuda} />}
        {compacto && error && <p className="m-0 mt-1 text-xs text-error">{error}</p>}
      </div>
    );
  }
  return (
    <div className={cn('min-w-0', className)}>
      <div className={cn('tg-campo', compacto && 'tg-campo-compacto')} data-lleno={prefijo ? 'true' : undefined}>
        <input
          ref={ref}
          id={campoId}
          placeholder=" "
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? `${campoId}-error` : ayuda ? `${campoId}-ayuda` : undefined}
          aria-label={compacto ? etiqueta : undefined}
          className={cn('tg-control', sufijo && 'tg-con-sufijo', prefijo && 'tg-con-prefijo', props.type === 'number' && 'text-right tabular-nums')}
          {...props}
        />
        <label htmlFor={campoId}>{etiqueta}</label>
        {prefijo && (
          <span className="tg-prefijo pointer-events-none text-base text-muted">{prefijo}</span>
        )}
        {sufijo && (
          <span className="tg-sufijo flex items-center text-sm text-muted">{sufijo}</span>
        )}
      </div>
      {!compacto && <PieCampo id={campoId} error={error} ayuda={ayuda} />}
      {compacto && error && <p className="m-0 mt-1 text-xs text-error">{error}</p>}
    </div>
  );
});

export type AreaTextoProps = PropsBaseCampo & Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'className'>;

export const AreaTexto = forwardRef<HTMLTextAreaElement, AreaTextoProps>(function AreaTexto(
  { etiqueta, error, ayuda, className, id, ...props },
  ref,
) {
  const generado = useId();
  const campoId = id ?? generado;
  return (
    <div className={cn('min-w-0', className)}>
      <div className="tg-campo">
        <textarea
          ref={ref}
          id={campoId}
          placeholder=" "
          aria-invalid={error ? true : undefined}
          className="tg-control"
          rows={3}
          {...props}
        />
        <label htmlFor={campoId}>{etiqueta}</label>
      </div>
      <PieCampo id={campoId} error={error} ayuda={ayuda} />
    </div>
  );
});

export type SelectorProps = PropsBaseCampo &
  Omit<SelectHTMLAttributes<HTMLSelectElement>, 'className'> & {
    opciones: { valor: string; etiqueta: string; deshabilitada?: boolean }[];
    /** Texto de la opción vacía; si se omite no hay opción vacía. */
    vacio?: string;
  };

/** Lista desplegable nativa en píldora (accesible y cómoda en tabletas). */
export const Selector = forwardRef<HTMLSelectElement, SelectorProps>(function Selector(
  { etiqueta, error, ayuda, className, compacto, opciones, vacio, id, ...props },
  ref,
) {
  const generado = useId();
  const campoId = id ?? generado;
  return (
    <div className={cn('min-w-0', className)}>
      <div className={cn('tg-campo', compacto && 'tg-campo-compacto')} data-lleno="true">
        <select
          ref={ref}
          id={campoId}
          aria-invalid={error ? true : undefined}
          aria-label={compacto ? etiqueta : undefined}
          className="tg-control appearance-none pr-12"
          {...props}
        >
          {vacio !== undefined && <option value="">{vacio}</option>}
          {opciones.map((o) => (
            <option key={o.valor} value={o.valor} disabled={o.deshabilitada}>
              {o.etiqueta}
            </option>
          ))}
        </select>
        <label htmlFor={campoId}>{etiqueta}</label>
        <Icono nombre="arrow_drop_down" className="pointer-events-none absolute top-1/2 right-4 -translate-y-1/2 text-muted" />
      </div>
      {!compacto && <PieCampo id={campoId} error={error} ayuda={ayuda} />}
    </div>
  );
});

/** Casilla de verificación con etiqueta. */
export const Casilla = forwardRef<
  HTMLInputElement,
  Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { etiqueta: ReactNode; descripcion?: string }
>(function Casilla({ etiqueta, descripcion, className, id, ...props }, ref) {
  const generado = useId();
  const campoId = id ?? generado;
  return (
    <label htmlFor={campoId} className={cn('flex cursor-pointer items-start gap-3 py-1', className)}>
      <input
        ref={ref}
        id={campoId}
        type="checkbox"
        className="mt-0.5 size-5 shrink-0 cursor-pointer accent-[var(--tg-primary)]"
        {...props}
      />
      <span>
        <span className="block">{etiqueta}</span>
        {descripcion && <span className="block text-xs text-muted">{descripcion}</span>}
      </span>
    </label>
  );
});

/** Interruptor (switch) con etiqueta. */
export const Interruptor = forwardRef<
  HTMLInputElement,
  Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { etiqueta: ReactNode }
>(function Interruptor({ etiqueta, className, id, ...props }, ref) {
  const generado = useId();
  const campoId = id ?? generado;
  return (
    <label htmlFor={campoId} className={cn('inline-flex cursor-pointer items-center gap-3', className)}>
      <span className="relative inline-flex">
        <input ref={ref} id={campoId} type="checkbox" role="switch" className="peer sr-only" {...props} />
        <span className="h-8 w-[52px] rounded-full border-2 border-outline bg-container-high transition-colors peer-checked:border-primary peer-checked:bg-primary peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-primary" />
        <span className="absolute top-2 left-2 size-4 rounded-full bg-outline transition-all peer-checked:top-1 peer-checked:left-6 peer-checked:size-6 peer-checked:bg-on-primary" />
      </span>
      <span>{etiqueta}</span>
    </label>
  );
});

export interface OpcionBusqueda {
  valor: string;
  etiqueta: string;
  detalle?: string;
}

/**
 * Selector con búsqueda (producto, cliente, proveedor). `buscar` puede ser local o consultar el API; se llama con el
 * texto escrito. `seleccion` es la opción elegida (para mostrar su etiqueta sin volver a buscar).
 */
export function BuscadorSeleccion({
  etiqueta,
  valor,
  seleccion,
  onCambiar,
  opciones,
  buscar,
  error,
  ayuda,
  className,
  compacto,
  deshabilitado,
  placeholderBusqueda = 'Escribe para buscar…',
}: PropsBaseCampo & {
  valor: string | null | undefined;
  seleccion?: OpcionBusqueda | null;
  onCambiar: (valor: string | null, opcion: OpcionBusqueda | null) => void;
  opciones?: OpcionBusqueda[];
  buscar?: (texto: string) => Promise<OpcionBusqueda[]>;
  deshabilitado?: boolean;
  placeholderBusqueda?: string;
}) {
  const id = useId();
  const [abierto, setAbierto] = useState(false);
  const [texto, setTexto] = useState('');
  const [remotas, setRemotas] = useState<OpcionBusqueda[] | null>(null);
  const [cargando, setCargando] = useState(false);
  const [activo, setActivo] = useState(0);
  const lista = useRef<HTMLUListElement>(null);

  useEffect(() => {
    if (!buscar || !abierto) return;
    let vigente = true;
    setCargando(true);
    const t = setTimeout(() => {
      buscar(texto)
        .then((r) => vigente && setRemotas(r))
        .finally(() => vigente && setCargando(false));
    }, 200);
    return () => {
      vigente = false;
      clearTimeout(t);
    };
  }, [texto, buscar, abierto]);

  const filtradas = useMemo(() => {
    if (buscar) return remotas ?? [];
    const t = texto.trim().toLowerCase();
    const todas = opciones ?? [];
    return t ? todas.filter((o) => `${o.etiqueta} ${o.detalle ?? ''}`.toLowerCase().includes(t)) : todas;
  }, [buscar, remotas, opciones, texto]);

  const actual = seleccion ?? opciones?.find((o) => o.valor === valor) ?? null;

  const elegir = (o: OpcionBusqueda | null) => {
    onCambiar(o?.valor ?? null, o);
    setAbierto(false);
    setTexto('');
  };

  return (
    <div className={cn('min-w-0', className)}>
      <Popover.Root open={abierto} onOpenChange={(a) => !deshabilitado && setAbierto(a)}>
        <Popover.Trigger asChild>
          <button
            type="button"
            id={id}
            disabled={deshabilitado}
            aria-invalid={error ? true : undefined}
            aria-label={compacto ? etiqueta : undefined}
            className={cn(
              'relative w-full border border-pill-border bg-pill text-left transition-colors hover:border-on-surface disabled:cursor-not-allowed disabled:opacity-60',
              compacto ? 'min-h-11 rounded-xl px-3.5 py-2.5' : 'min-h-14 rounded-[24px] px-5 pt-5 pb-1.5',
              error && 'border-error',
              abierto && 'border-primary shadow-[inset_0_0_0_1px_var(--tg-primary)]',
            )}
          >
            {!compacto && (
              <span
                className={cn(
                  'pointer-events-none absolute left-5 text-muted transition-all',
                  actual ? 'top-[7px] text-xs' : 'top-[17px] text-base',
                )}
              >
                {etiqueta}
              </span>
            )}
            <span className={cn('block truncate pr-8 text-base', !actual && compacto && 'text-muted')}>
              {actual ? actual.etiqueta : compacto ? etiqueta : ' '}
            </span>
            <Icono nombre="search" className="absolute top-1/2 right-4 -translate-y-1/2 text-muted" tamano={22} />
          </button>
        </Popover.Trigger>
        <Popover.Portal>
          <Popover.Content
            align="start"
            sideOffset={4}
            className="z-50 w-[var(--radix-popover-trigger-width)] min-w-72 overflow-hidden rounded-xl border border-outline-variant bg-card shadow-[0_8px_24px_rgb(0_0_0/18%)]"
            onOpenAutoFocus={(e) => {
              e.preventDefault();
              (e.currentTarget as HTMLElement).querySelector('input')?.focus();
            }}
          >
            <div className="border-b border-row-divider p-2">
              <input
                value={texto}
                onChange={(e) => {
                  setTexto(e.target.value);
                  setActivo(0);
                }}
                onKeyDown={(e) => {
                  if (e.key === 'ArrowDown') {
                    e.preventDefault();
                    setActivo((a) => Math.min(a + 1, filtradas.length - 1));
                  } else if (e.key === 'ArrowUp') {
                    e.preventDefault();
                    setActivo((a) => Math.max(a - 1, 0));
                  } else if (e.key === 'Enter') {
                    e.preventDefault();
                    if (filtradas[activo]) elegir(filtradas[activo]);
                  }
                }}
                placeholder={placeholderBusqueda}
                aria-label={`Buscar ${etiqueta.toLowerCase()}`}
                className="h-10 w-full rounded-full border border-pill-border bg-pill px-4 text-sm outline-none focus:border-primary"
              />
            </div>
            <ul ref={lista} role="listbox" aria-label={etiqueta} className="m-0 max-h-72 list-none overflow-y-auto p-1">
              {cargando && (
                <li className="flex items-center gap-2 px-3 py-2 text-sm text-muted">
                  <Girador className="size-4" /> Buscando…
                </li>
              )}
              {!cargando && filtradas.length === 0 && <li className="px-3 py-2 text-sm text-muted">Sin resultados.</li>}
              {filtradas.map((o, i) => (
                <li
                  key={o.valor}
                  role="option"
                  aria-selected={o.valor === valor}
                  onMouseEnter={() => setActivo(i)}
                  onClick={() => elegir(o)}
                  className={cn(
                    'cursor-pointer rounded-lg px-3 py-2',
                    i === activo && 'bg-row-hover',
                    o.valor === valor && 'font-medium text-primary',
                  )}
                >
                  <span className="block">{o.etiqueta}</span>
                  {o.detalle && <span className="block text-xs text-muted">{o.detalle}</span>}
                </li>
              ))}
            </ul>
            {valor && (
              <div className="border-t border-row-divider p-1">
                <button
                  type="button"
                  onClick={() => elegir(null)}
                  className="w-full rounded-lg px-3 py-2 text-left text-sm text-muted hover:bg-row-hover"
                >
                  Quitar selección
                </button>
              </div>
            )}
          </Popover.Content>
        </Popover.Portal>
      </Popover.Root>
      {!compacto && <PieCampo id={id} error={error} ayuda={ayuda} />}
      {compacto && error && <p className="m-0 mt-1 text-xs text-error">{error}</p>}
    </div>
  );
}
