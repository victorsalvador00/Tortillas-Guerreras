import { Redirect, Stack } from 'expo-router';
import { useSesion } from '@/servicios/contextoSesion';
import { ProveedorSync } from '@/servicios/sincronizacion';
import { Logo } from '@/ui/Logo';
import { useColores } from '@/ui/tema';

/** Pantallas de trabajo: requieren sesión con dispositivo aprobado; la sincronización vive aquí. */
export default function LayoutApp() {
  const c = useColores();
  const { estado, expirar } = useSesion();
  if (estado === 'cargando') return null;
  if (estado === 'sinSesion') return <Redirect href="/login" />;
  if (estado === 'espera') return <Redirect href="/espera" />;

  return (
    <ProveedorSync alRequerirLogin={expirar}>
      <Stack
        screenOptions={{
          headerStyle: { backgroundColor: c.barra },
          headerTintColor: c.barraTexto,
          headerTitleStyle: { fontWeight: '600' },
          contentStyle: { backgroundColor: c.fondo },
        }}
      >
        <Stack.Screen name="index" options={{ title: 'Mi día', headerTitle: () => <Logo ancho={130} /> }} />
        <Stack.Screen name="clientes/index" options={{ title: 'Clientes de hoy' }} />
        <Stack.Screen name="clientes/[id]" options={{ title: 'Cliente' }} />
        <Stack.Screen name="venta/[clienteId]" options={{ title: 'Nueva venta' }} />
        <Stack.Screen name="entrega/[pedidoId]" options={{ title: 'Entregar pedido' }} />
        <Stack.Screen name="retorno/[clienteId]" options={{ title: 'Retorno' }} />
        <Stack.Screen name="pedido/[clienteId]" options={{ title: 'Nuevo pedido' }} />
        <Stack.Screen name="pedidos" options={{ title: 'Pedidos' }} />
        <Stack.Screen name="cambio/[clienteId]" options={{ title: 'Cambio físico' }} />
        <Stack.Screen name="ticket/[ventaId]" options={{ title: 'Ticket' }} />
        <Stack.Screen name="inventario" options={{ title: 'Inventario de camioneta' }} />
        <Stack.Screen name="pendientes" options={{ title: 'Pendientes' }} />
      </Stack>
    </ProveedorSync>
  );
}
