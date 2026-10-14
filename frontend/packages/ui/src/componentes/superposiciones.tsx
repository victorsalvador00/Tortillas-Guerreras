import * as Dialog from '@radix-ui/react-dialog';
import * as Menu from '@radix-ui/react-dropdown-menu';
import * as Tabs from '@radix-ui/react-tabs';
import { useState, type ReactNode } from 'react';
import { Toaster as Sonner } from 'sonner';
import { cn } from '../utils';
import { Aviso, Boton, BotonIcono, Icono } from './basicos';

/** Diálogo modal con título, contenido desplazable y acciones al pie. */
export function Dialogo({
  abierto,
  onCambiar,
  titulo,
  descripcion,
  children,
  acciones,
  ancho = 'normal',
}: {
  abierto: boolean;
  onCambiar: (abierto: boolean) => void;
  titulo: string;
  descripcion?: ReactNode;
  children?: ReactNode;
  acciones?: ReactNode;
  ancho?: 'chico' | 'normal' | 'grande';
}) {
  return (
    <Dialog.Root open={abierto} onOpenChange={onCambiar}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-[var(--tg-scrim)] data-[state=open]:animate-in" />
        <Dialog.Content
          className={cn(
            'fixed top-1/2 left-1/2 z-50 flex max-h-[90vh] w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 flex-col rounded-[28px] border border-card-border bg-card shadow-[0_12px_40px_rgb(0_0_0/25%)]',
            ancho === 'chico' && 'max-w-md',
            ancho === 'normal' && 'max-w-xl',
            ancho === 'grande' && 'max-w-4xl',
          )}
        >
          <div className="flex items-start justify-between gap-4 px-6 pt-6">
            <div>
              <Dialog.Title className="m-0 text-[22px] font-normal">{titulo}</Dialog.Title>
              {descripcion ? (
                <Dialog.Description className="m-0 mt-1 text-sm text-muted">{descripcion}</Dialog.Description>
              ) : (
                <Dialog.Description className="sr-only">{titulo}</Dialog.Description>
              )}
            </div>
            <Dialog.Close asChild>
              <BotonIcono icono="close" etiqueta="Cerrar" plano />
            </Dialog.Close>
          </div>
          {children && <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4">{children}</div>}
          {acciones && <div className="flex flex-wrap justify-end gap-2 px-6 pt-2 pb-6">{acciones}</div>}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/**
 * Confirmación de cambios de estado (07-web: "toda acción de cambio de estado pide confirmación"). Ejecuta
 * `alConfirmar` y muestra el error si falla; el diálogo se cierra solo si termina bien.
 */
export function DialogoConfirmacion({
  abierto,
  onCambiar,
  titulo,
  mensaje,
  textoConfirmar = 'Confirmar',
  peligro,
  alConfirmar,
  children,
}: {
  abierto: boolean;
  onCambiar: (abierto: boolean) => void;
  titulo: string;
  mensaje: ReactNode;
  textoConfirmar?: string;
  peligro?: boolean;
  alConfirmar: () => Promise<unknown>;
  children?: ReactNode;
}) {
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={(a) => {
        if (!cargando) {
          setError(null);
          onCambiar(a);
        }
      }}
      titulo={titulo}
      ancho="chico"
      acciones={
        <>
          <Boton variante="texto" onClick={() => onCambiar(false)} disabled={cargando}>
            Cancelar
          </Boton>
          <Boton
            variante={peligro ? 'peligro' : 'primario'}
            cargando={cargando}
            onClick={async () => {
              setCargando(true);
              setError(null);
              try {
                await alConfirmar();
                onCambiar(false);
              } catch (e) {
                setError(e instanceof Error ? e.message : 'Ocurrió un error inesperado.');
              } finally {
                setCargando(false);
              }
            }}
          >
            {textoConfirmar}
          </Boton>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <div className="text-[15px]">{mensaje}</div>
        {children}
        {error && <Aviso tipo="error">{error}</Aviso>}
      </div>
    </Dialogo>
  );
}

export interface OpcionMenu {
  etiqueta: string;
  icono?: string;
  alSeleccionar: () => void;
  marcada?: boolean;
  peligro?: boolean;
  deshabilitada?: boolean;
}

/** Menú desplegable (acciones de fila, menú de usuario). */
export function MenuAcciones({
  disparador,
  opciones,
  encabezado,
  alinear = 'end',
}: {
  disparador: ReactNode;
  opciones: (OpcionMenu | 'separador' | { titulo: string })[];
  encabezado?: ReactNode;
  alinear?: 'start' | 'end';
}) {
  return (
    <Menu.Root>
      <Menu.Trigger asChild>{disparador}</Menu.Trigger>
      <Menu.Portal>
        <Menu.Content
          align={alinear}
          sideOffset={6}
          className="z-50 min-w-56 rounded-xl border border-outline-variant bg-card py-2 text-on-surface shadow-[0_8px_24px_rgb(0_0_0/18%)]"
        >
          {encabezado && <div className="px-4 pt-1 pb-2">{encabezado}</div>}
          {opciones.map((o, i) => {
            if (o === 'separador') return <Menu.Separator key={i} className="my-1 h-px bg-row-divider" />;
            if ('titulo' in o)
              return (
                <Menu.Label key={i} className="px-4 pt-2 pb-1 text-xs font-medium text-muted">
                  {o.titulo}
                </Menu.Label>
              );
            return (
              <Menu.Item
                key={i}
                disabled={o.deshabilitada}
                onSelect={o.alSeleccionar}
                className={cn(
                  'flex min-h-11 cursor-pointer items-center gap-3 px-4 text-sm outline-none data-[disabled]:opacity-50 data-[highlighted]:bg-row-hover',
                  o.peligro && 'text-error',
                )}
              >
                {o.icono && <Icono nombre={o.icono} tamano={20} className="text-muted" />}
                <span className="flex-1">{o.etiqueta}</span>
                {o.marcada && <Icono nombre="check" tamano={20} className="text-primary" />}
              </Menu.Item>
            );
          })}
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
}

/** Pestañas con subrayado de 3 px. */
export function Pestanas({
  valor,
  onCambiar,
  pestanas,
  className,
}: {
  valor: string;
  onCambiar: (valor: string) => void;
  pestanas: { valor: string; etiqueta: ReactNode; contenido: ReactNode }[];
  className?: string;
}) {
  return (
    <Tabs.Root value={valor} onValueChange={onCambiar} className={className}>
      <Tabs.List className="mb-4 flex gap-1 overflow-x-auto border-b border-row-divider">
        {pestanas.map((p) => (
          <Tabs.Trigger
            key={p.valor}
            value={p.valor}
            className="relative h-12 shrink-0 px-4 text-sm font-medium text-muted transition-colors hover:text-on-surface data-[state=active]:text-primary data-[state=active]:after:absolute data-[state=active]:after:inset-x-2 data-[state=active]:after:bottom-0 data-[state=active]:after:h-[3px] data-[state=active]:after:rounded-t-full data-[state=active]:after:bg-primary"
          >
            {p.etiqueta}
          </Tabs.Trigger>
        ))}
      </Tabs.List>
      {pestanas.map((p) => (
        <Tabs.Content key={p.valor} value={p.valor}>
          {p.contenido}
        </Tabs.Content>
      ))}
    </Tabs.Root>
  );
}

/** Avisos flotantes (toasts). */
export function Avisos() {
  return (
    <Sonner
      position="bottom-center"
      richColors
      closeButton
      toastOptions={{ style: { borderRadius: 12, fontFamily: 'inherit' } }}
    />
  );
}

export { toast } from 'sonner';
