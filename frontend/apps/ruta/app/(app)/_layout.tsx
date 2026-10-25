import { Redirect, Stack } from "expo-router";
import { useSesion } from "@/servicios/contextoSesion";
import { ProveedorSync } from "@/servicios/sincronizacion";
import { Encabezado } from "@/ui/Encabezado";
import { Logo } from "@/ui/Logo";
import { BotonMenu, ProveedorMenuLateral } from "@/ui/MenuLateral";
import { useColores } from "@/ui/tema";

/**
 * Pantallas de trabajo: requieren sesión con dispositivo aprobado; la sincronización vive aquí. Las pantallas
 * principales llevan el botón del menú lateral; las de captura, la flecha de regreso.
 */
export default function LayoutApp() {
  const c = useColores();
  const { estado, expirar } = useSesion();
  if (estado === "cargando") return null;
  if (estado === "sinSesion") return <Redirect href="/login" />;
  if (estado === "espera") return <Redirect href="/espera" />;

  return (
    <ProveedorSync alRequerirLogin={expirar}>
      <ProveedorMenuLateral>
        <Stack
          screenOptions={{
            // Con edge-to-edge el encabezado nativo de esta pila anidada no reservaba la barra de estado: la flecha y
            // el título quedaban debajo de ella y no recibían toques. El encabezado propio aplica el área segura.
            header: (props) => <Encabezado {...props} />,
            headerStyle: { backgroundColor: c.barra },
            headerTintColor: c.barraTexto,
            headerTitleStyle: { fontWeight: "600" },
            contentStyle: { backgroundColor: c.fondo },
          }}
        >
          <Stack.Screen
            name="index"
            options={{
              headerLeft: () => <BotonMenu />,
              title: "Mi día",
              headerTitle: () => <Logo ancho={130} />,
            }}
          />
          <Stack.Screen
            name="clientes/index"
            options={{
              headerLeft: () => <BotonMenu />,
              title: "Clientes de hoy",
            }}
          />
          <Stack.Screen name="clientes/[id]" options={{ title: "Cliente" }} />
          <Stack.Screen
            name="venta/[clienteId]"
            options={{ title: "Nueva venta" }}
          />
          <Stack.Screen
            name="entrega/[pedidoId]"
            options={{ title: "Entregar pedido" }}
          />
          <Stack.Screen
            name="retorno/[clienteId]"
            options={{ title: "Retorno" }}
          />
          <Stack.Screen
            name="pedido/[clienteId]"
            options={{ title: "Nuevo pedido" }}
          />
          <Stack.Screen
            name="pedidos"
            options={{ headerLeft: () => <BotonMenu />, title: "Pedidos" }}
          />
          <Stack.Screen
            name="cambio/[clienteId]"
            options={{ title: "Cambio físico" }}
          />
          <Stack.Screen name="ticket/[ventaId]" options={{ title: "Ticket" }} />
          <Stack.Screen
            name="inventario"
            options={{
              headerLeft: () => <BotonMenu />,
              title: "Inventario de camioneta",
            }}
          />
          <Stack.Screen
            name="pendientes"
            options={{ headerLeft: () => <BotonMenu />, title: "Pendientes" }}
          />
        </Stack>
      </ProveedorMenuLateral>
    </ProveedorSync>
  );
}
