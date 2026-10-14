import { useListarLineas, type Turno } from '@tg/api-client';
import { Selector } from '@tg/ui';

export const TURNOS: { valor: Turno; etiqueta: string }[] = [
  { valor: 'Matutino', etiqueta: 'Matutino' },
  { valor: 'Vespertino', etiqueta: 'Vespertino' },
  { valor: 'Nocturno', etiqueta: 'Nocturno' },
];

export const ESTADOS_ORDEN = [
  { valor: 'Planeada', etiqueta: 'Planeada' },
  { valor: 'Liberada', etiqueta: 'Liberada' },
  { valor: 'EnProceso', etiqueta: 'En proceso' },
  { valor: 'Terminada', etiqueta: 'Terminada' },
  { valor: 'Cancelada', etiqueta: 'Cancelada' },
];

export function SelectorLinea({
  value,
  onChange,
  vacio = 'Sin línea',
  className,
}: {
  value: string;
  onChange: (v: string) => void;
  vacio?: string;
  className?: string;
}) {
  const { data } = useListarLineas({ activo: true });
  return (
    <Selector
      etiqueta="Línea"
      vacio={vacio}
      className={className}
      opciones={(data ?? []).map((l) => ({ valor: l.id, etiqueta: `${l.codigo} · ${l.nombre}` }))}
      value={value}
      onChange={(e) => onChange(e.target.value)}
    />
  );
}
