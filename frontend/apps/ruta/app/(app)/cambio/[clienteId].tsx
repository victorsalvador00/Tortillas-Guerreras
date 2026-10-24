import { fechaOperativa, formatoCantidad, formatoFecha } from '@tg/shared';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { View } from 'react-native';
import { cliente, motivos, parametro, productosParaVenta, referenciaCantidades } from '@/db/consultas';
import { registrarCambioFisico } from '@/db/operaciones';
import { pendientesCambio } from '@/dominio/pedidos';
import { validarExistencia } from '@/dominio/ventas';
import { leerGps } from '@/servicios/gps';
import { useSync } from '@/servicios/sincronizacion';
import { AvisoCantidades, useCantidadesConfiables } from '@/ui/cantidades';
import { Aviso, Boton, Campo, Cantidad, Opciones, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

type Condicion = 'Vendible' | 'NoVendible';
type Lista = 'Retirar' | 'Entregar';

interface Retiro {
  cantidad: number;
  condicion: Condicion;
  motivoId: string | null;
}

/**
 * Cambio físico (06, v0.2): **Retirar** (producto, cantidad, condición —por defecto no vendible [SUP-25]— y motivo) y
 * **Entregar** (de la existencia de la camioneta, RN-APP-02). Lo que se entrega de menos queda pendiente salvo "No
 * dejar pendiente" (RN-LOG-16). Los cambios pendientes del cliente se entregan aquí y se saldan (RN-LOG-17).
 */
export default function CambioFisico() {
  const { clienteId, pendienteId } = useLocalSearchParams<{ clienteId: string; pendienteId?: string }>();
  const { notificarCambio, sincronizar } = useSync();
  const datos = useDatos(() => {
    const c = cliente(clienteId);
    return {
      cliente: c,
      productos: c ? productosParaVenta(c, fechaOperativa()) : [],
      motivos: motivos('retorno'),
      permiteNegativo: parametro('ruta.permite_existencia_negativa', true),
      referencia: referenciaCantidades(clienteId),
    };
  });
  const c = datos.cliente;
  const pendientesCliente = c?.cambiosPendientes ?? [];
  const motivoNoVendible = datos.motivos.find((m) => m.clave === 'DEV_NO_VENDIBLE')?.id ?? datos.motivos[0]?.id ?? null;
  const [lista, setLista] = useState<Lista>(pendienteId ? 'Entregar' : 'Retirar');
  const [retiros, setRetiros] = useState<Record<string, Retiro>>({});
  const [entregas, setEntregas] = useState<Record<string, number>>({});
  const [saldos, setSaldos] = useState<Record<string, number>>(() => {
    const p = pendientesCliente.find((x) => x.id === pendienteId);
    return p ? { [p.id]: p.restante } : {};
  });
  const [dejarPendiente, setDejarPendiente] = useState(true);
  const [buscar, setBuscar] = useState('');
  const [confirmandoFaltante, setConfirmandoFaltante] = useState(false);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const nombre = (id: string) => datos.productos.find((p) => p.productoId === id)?.nombreCorto ?? pendientesCliente.find((p) => p.productoId === id)?.nombre ?? 'Producto';
  const unidad = (id: string) => datos.productos.find((p) => p.productoId === id)?.unidadBase ?? '';
  const retirar = Object.entries(retiros)
    .filter(([, r]) => r.cantidad > 0)
    .map(([productoId, r]) => ({ productoId, cantidadBase: r.cantidad, condicion: r.condicion, motivoMermaId: r.condicion === 'NoVendible' ? r.motivoId : null }));
  const entregar = [
    ...Object.entries(entregas)
      .filter(([, n]) => n > 0)
      .map(([productoId, n]) => ({ productoId, cantidadBase: n, cambioPendienteId: null as string | null })),
    ...Object.entries(saldos)
      .filter(([, n]) => n > 0)
      .flatMap(([id, n]) => {
        const p = pendientesCliente.find((x) => x.id === id);
        return p ? [{ productoId: p.productoId, cantidadBase: n, cambioPendienteId: id as string | null }] : [];
      }),
  ];
  const quedan = pendientesCambio(retirar, entregar);
  const totalQuedan = [...quedan.values()].reduce((s, n) => s + n, 0);
  const existencias = useMemo(() => new Map(datos.productos.map((p) => [p.productoId, p.existencia])), [datos.productos]);
  const validacion = validarExistencia(entregar, existencias, datos.permiteNegativo);
  const cantidadesConfiables = useCantidadesConfiables(
    [...retirar, ...entregar].map((p) => ({ productoId: p.productoId, descripcion: nombre(p.productoId), cantidad: p.cantidadBase, unidadClave: unidad(p.productoId), factorABase: 1 })),
    datos.referencia,
  );

  if (!c) return <Vacio texto="El cliente no está en tu ruta." />;
  if (c.esGenerico) return <Vacio texto="El cambio físico es por cliente; elige la tienda." />;

  const cambio = () => {
    setConfirmandoFaltante(false);
    cantidadesConfiables.reiniciar();
  };

  const actualizarRetiro = (productoId: string, parcial: Partial<Retiro>) => {
    cambio();
    setRetiros((s) => {
      // [SUP-25] Lo retirado es no vendible por defecto.
      const actual = s[productoId] ?? { cantidad: 0, condicion: 'NoVendible', motivoId: motivoNoVendible };
      const nuevo = { ...actual, ...parcial };
      if (nuevo.condicion === 'NoVendible' && !nuevo.motivoId) nuevo.motivoId = motivoNoVendible;
      return { ...s, [productoId]: nuevo };
    });
  };

  const confirmar = async () => {
    setError(null);
    if (retirar.length === 0 && entregar.length === 0) {
      setError('Captura lo que retiras o lo que entregas.');
      return;
    }
    if (retirar.some((r) => r.condicion === 'NoVendible' && !r.motivoMermaId)) {
      setError('Elige el motivo de lo no vendible.');
      return;
    }
    if (!validacion.permitido) {
      setError('No hay existencia suficiente en la camioneta para lo que entregas.');
      return;
    }
    if (validacion.faltantes.length > 0 && !confirmandoFaltante) {
      setConfirmandoFaltante(true);
      return;
    }
    if (!cantidadesConfiables.revisar()) return;
    setGuardando(true);
    try {
      const gps = await leerGps();
      registrarCambioFisico({ clienteId: c.id, retirar, entregar, dejarPendiente, gps });
      notificarCambio();
      void sincronizar();
      router.back();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar el cambio.');
      setGuardando(false);
    }
  };

  const visibles = datos.productos.filter((p) => {
    const t = buscar.trim().toLowerCase();
    return !t || `${p.nombre} ${p.nombreCorto} ${p.sku}`.toLowerCase().includes(t);
  });

  return (
    <Pantalla
      pie={
        <>
          <Texto suave>
            Retiras {retirar.length} producto(s) · entregas {entregar.length}
          </Texto>
          {totalQuedan > 0 && (
            <View style={{ gap: 8 }}>
              <Aviso tipo="amarillo">
                Quedan {formatoCantidad(totalQuedan, 3)} pendientes ({[...quedan].map(([id, n]) => `${nombre(id)} ${formatoCantidad(n, 3)}`).join(', ')}).
              </Aviso>
              <Opciones
                opciones={[
                  { valor: 'si', texto: 'Dejar pendiente' },
                  { valor: 'no', texto: 'No dejar pendiente' },
                ]}
                valor={dejarPendiente ? 'si' : 'no'}
                alCambiar={(v) => setDejarPendiente(v === 'si')}
              />
            </View>
          )}
          {confirmandoFaltante && (
            <Aviso tipo="naranja">
              La camioneta quedará en negativo en: {validacion.faltantes.map((f) => nombre(f.productoId)).join(', ')}. Toca de nuevo para confirmar.
            </Aviso>
          )}
          {cantidadesConfiables.pendiente && <AvisoCantidades alertas={cantidadesConfiables.alertas} />}
          {error && <Aviso tipo="rojo">{error}</Aviso>}
          <Boton
            texto={guardando ? 'Tomando ubicación…' : confirmandoFaltante || cantidadesConfiables.pendiente ? 'Confirmar de todos modos' : 'Confirmar cambio'}
            icono="swap-horiz"
            grande
            cargando={guardando}
            alPresionar={confirmar}
          />
        </>
      }
    >
      <Tarjeta>
        <Texto negrita tamano={17}>
          {c.nombre}
        </Texto>
        <Opciones<Lista>
          opciones={[
            { valor: 'Retirar', texto: `Retirar (${retirar.length})` },
            { valor: 'Entregar', texto: `Entregar (${entregar.length})` },
          ]}
          valor={lista}
          alCambiar={setLista}
        />
        <Texto suave>
          {lista === 'Retirar'
            ? 'Producto que recoges del cliente. Lo no vendible es merma.'
            : 'Producto que dejas de la camioneta a cambio; no genera venta.'}
        </Texto>
      </Tarjeta>

      {lista === 'Entregar' && pendientesCliente.length > 0 && (
        <Tarjeta titulo="Cambios pendientes del cliente">
          {pendientesCliente.map((p) => (
            <View key={p.id} style={{ gap: 6 }}>
              <View style={estilos.filaEntre}>
                <Texto negrita>{p.nombre}</Texto>
                <Texto suave>
                  {formatoCantidad(p.restante, 3)} {p.unidad} desde {formatoFecha(p.fechaOperativa)}
                </Texto>
              </View>
              <Cantidad
                valor={saldos[p.id] ?? 0}
                decimales={3}
                alCambiar={(v) => {
                  cambio();
                  setSaldos((s) => ({ ...s, [p.id]: Math.min(v, p.restante) }));
                }}
              />
            </View>
          ))}
        </Tarjeta>
      )}

      <Campo etiqueta="Buscar producto" value={buscar} onChangeText={setBuscar} />
      {visibles.map((p) => {
        if (lista === 'Entregar') {
          const faltante = validacion.faltantes.some((f) => f.productoId === p.productoId);
          return (
            <Tarjeta key={p.productoId}>
              <View style={estilos.filaEntre}>
                <Texto negrita>{p.nombreCorto}</Texto>
                <Texto suave>
                  hay {formatoCantidad(p.existencia, 3)} {p.unidadBase}
                </Texto>
              </View>
              <Cantidad
                valor={entregas[p.productoId] ?? 0}
                decimales={3}
                alCambiar={(v) => {
                  cambio();
                  setEntregas((s) => ({ ...s, [p.productoId]: v }));
                }}
              />
              {faltante && <Texto estilo={{ color: '#a64200' }}>Más de lo que hay en la camioneta.</Texto>}
            </Tarjeta>
          );
        }
        const r = retiros[p.productoId];
        return (
          <Tarjeta key={p.productoId}>
            <Texto negrita>
              {p.nombreCorto} ({p.unidadBase})
            </Texto>
            <Cantidad valor={r?.cantidad ?? 0} decimales={3} alCambiar={(v) => actualizarRetiro(p.productoId, { cantidad: v })} />
            {(r?.cantidad ?? 0) > 0 && (
              <View style={{ gap: 8 }}>
                <Opciones<Condicion>
                  opciones={[
                    { valor: 'NoVendible', texto: 'No vendible' },
                    { valor: 'Vendible', texto: 'Vendible' },
                  ]}
                  valor={r!.condicion}
                  alCambiar={(v) => actualizarRetiro(p.productoId, { condicion: v })}
                />
                {r!.condicion === 'NoVendible' && (
                  <Opciones
                    opciones={datos.motivos.map((m) => ({ valor: m.id, texto: m.nombre }))}
                    valor={r!.motivoId ?? ''}
                    alCambiar={(v) => actualizarRetiro(p.productoId, { motivoId: v })}
                  />
                )}
              </View>
            )}
          </Tarjeta>
        );
      })}
    </Pantalla>
  );
}
