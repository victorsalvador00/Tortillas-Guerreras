import { useListarProveedores } from '@tg/api-client';
import { Selector } from '@tg/ui';

export const ESTADOS_OC = [
  { valor: 'Borrador', etiqueta: 'Borrador' },
  { valor: 'Autorizada', etiqueta: 'Autorizada' },
  { valor: 'RecibidaParcial', etiqueta: 'Recibida parcial' },
  { valor: 'Recibida', etiqueta: 'Recibida' },
  { valor: 'Cerrada', etiqueta: 'Cerrada' },
  { valor: 'Cancelada', etiqueta: 'Cancelada' },
];

/** Lista de proveedores activos. */
export function SelectorProveedor({
  value,
  onChange,
  error,
  etiqueta = 'Proveedor',
  vacio = 'Elige un proveedor',
  className,
  disabled,
}: {
  value: string;
  onChange: (valor: string) => void;
  error?: string;
  etiqueta?: string;
  vacio?: string;
  className?: string;
  disabled?: boolean;
}) {
  const { data } = useListarProveedores({ activo: true, tamano: 200 });
  return (
    <Selector
      etiqueta={etiqueta}
      vacio={vacio}
      opciones={(data?.items ?? []).map((p) => ({ valor: p.id, etiqueta: `${p.codigo} · ${p.razonSocial}` }))}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      error={error}
      className={className}
      disabled={disabled}
    />
  );
}
