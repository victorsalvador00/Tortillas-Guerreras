import {
  forwardRef,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
  useState,
  type ChangeEvent,
  type FocusEvent,
  type InputHTMLAttributes,
} from 'react';
import { cn } from '../utils';
import { Icono } from './basicos';

/** "2026-11-03" → "03/11/2026". */
export function isoADmy(iso: string | null | undefined): string {
  if (!iso) return '';
  const [a, m, d] = iso.slice(0, 10).split('-');
  return a && m && d ? `${d}/${m}/${a}` : '';
}

/** "03/11/2026" → "2026-11-03"; `null` si no es una fecha válida. */
export function dmyAIso(texto: string): string | null {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(texto.trim());
  if (!m) return null;
  const [, d, mes, a] = m;
  const fecha = new Date(Date.UTC(Number(a), Number(mes) - 1, Number(d)));
  const valida = fecha.getUTCFullYear() === Number(a) && fecha.getUTCMonth() === Number(mes) - 1 && fecha.getUTCDate() === Number(d);
  return valida ? `${a}-${mes}-${d}` : null;
}

/** Inserta las diagonales mientras se escribe: "0311" → "03/11", "03112026" → "03/11/2026". */
export function mascaraDmy(texto: string): string {
  const digitos = texto.replace(/\D/g, '').slice(0, 8);
  if (digitos.length <= 2) return digitos;
  if (digitos.length <= 4) return `${digitos.slice(0, 2)}/${digitos.slice(2)}`;
  return `${digitos.slice(0, 2)}/${digitos.slice(2, 4)}/${digitos.slice(4)}`;
}

type PropsEntrada = Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>;

/**
 * Fecha siempre en DD/MM/AAAA, sin depender del idioma del navegador (el `<input type="date">` nativo la muestra en
 * MM/DD/AAAA en Chrome en inglés). El valor sigue siendo ISO (`yyyy-MM-dd`) en un input oculto que recibe `ref`,
 * `name`, `value`/`defaultValue`, `onChange` y `onBlur`, así que funciona igual con `register()` de React Hook Form
 * que controlado; el botón de calendario abre el selector nativo.
 */
export const EntradaFecha = forwardRef<
  HTMLInputElement,
  PropsEntrada & { claseVisible?: string; idVisible?: string; etiquetaCalendario?: string }
>(function EntradaFecha({ claseVisible, idVisible, etiquetaCalendario = 'Abrir calendario', onChange, onBlur, disabled, value, defaultValue, name, required, min, max, ...resto }, ref) {
  const oculto = useRef<HTMLInputElement>(null);
  useImperativeHandle(ref, () => oculto.current!, []);
  const [texto, setTexto] = useState(() => isoADmy((value ?? defaultValue ?? '') as string));
  const editando = useRef(false);
  const ultimoIso = useRef<string>((value ?? defaultValue ?? '') as string);

  // Sincroniza el texto cuando el valor cambia por fuera (controlado o `reset` de React Hook Form).
  useLayoutEffect(() => {
    const iso = value !== undefined ? String(value ?? '') : (oculto.current?.value ?? '');
    if (!editando.current && iso !== ultimoIso.current) {
      ultimoIso.current = iso;
      setTexto(isoADmy(iso));
    }
  });

  const emitir = (iso: string) => {
    const nodo = oculto.current;
    if (!nodo) return;
    ultimoIso.current = iso;
    // Valor nativo + evento sintético: React Hook Form y los manejadores controlados leen `target.value`.
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nodo, iso);
    onChange?.({ target: nodo, currentTarget: nodo, type: 'change' } as unknown as ChangeEvent<HTMLInputElement>);
  };

  return (
    <>
      <input
        id={idVisible}
        type="text"
        inputMode="numeric"
        autoComplete="off"
        placeholder="DD/MM/AAAA"
        maxLength={10}
        disabled={disabled}
        required={required}
        value={texto}
        className={claseVisible}
        {...resto}
        onFocus={(e) => {
          editando.current = true;
          resto.onFocus?.(e);
        }}
        onChange={(e) => {
          const nuevo = mascaraDmy(e.target.value);
          setTexto(nuevo);
          if (nuevo === '') emitir('');
          else {
            const iso = dmyAIso(nuevo);
            if (iso) emitir(iso);
          }
        }}
        onBlur={(e: FocusEvent<HTMLInputElement>) => {
          editando.current = false;
          // Texto incompleto o inválido: regresa al último valor válido.
          if (texto !== '' && !dmyAIso(texto)) setTexto(isoADmy(ultimoIso.current));
          const nodo = oculto.current;
          onBlur?.(nodo ? ({ ...e, target: nodo, currentTarget: nodo } as FocusEvent<HTMLInputElement>) : e);
        }}
      />
      <input
        ref={oculto}
        type="date"
        name={name}
        min={min}
        max={max}
        tabIndex={-1}
        aria-hidden
        disabled={disabled}
        {...(value !== undefined ? { value: (value ?? '') as string } : { defaultValue: defaultValue as string | undefined })}
        onChange={(e) => {
          ultimoIso.current = e.target.value;
          setTexto(isoADmy(e.target.value));
          onChange?.(e);
        }}
        className="pointer-events-none absolute right-2 bottom-0 h-px w-px opacity-0"
      />
      <button
        type="button"
        disabled={disabled}
        aria-label={etiquetaCalendario}
        title={etiquetaCalendario}
        onClick={() => {
          const nodo = oculto.current;
          if (!nodo) return;
          try {
            nodo.showPicker();
          } catch {
            nodo.focus();
          }
        }}
        className={cn(
          'absolute top-1/2 right-3 grid size-9 -translate-y-1/2 cursor-pointer place-items-center rounded-full text-muted hover:bg-row-hover disabled:cursor-not-allowed',
        )}
      >
        <Icono nombre="calendar_month" tamano={20} />
      </button>
    </>
  );
});
