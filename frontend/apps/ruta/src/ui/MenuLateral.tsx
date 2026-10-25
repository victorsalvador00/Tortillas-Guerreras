import { fechaOperativa } from '@tg/shared';
import { router, usePathname, type Href } from 'expo-router';
import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';
import { Animated, Easing, Modal, Pressable, ScrollView, Text, View, useWindowDimensions } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { canalRuta, clienteGenerico, estadoDispositivo, resumenDia } from '@/db/consultas';
import { useSesion } from '@/servicios/contextoSesion';
import { useSync } from '@/servicios/sincronizacion';
import { Icono, type NombreIcono } from './componentes';
import { Logo } from './Logo';
import { RADIO_PILDORA, useColores } from './tema';

const ContextoMenu = createContext<{ abrir: () => void }>({ abrir: () => {} });

/** Abre el menú lateral desde cualquier pantalla de trabajo. */
export const useMenuLateral = () => useContext(ContextoMenu);

const DURACION = 220;

/**
 * Menú lateral de la app de ruta: encabezado con logo, chofer y ruta; accesos a las pantallas principales y cerrar
 * sesión. Hecho con `Modal` y `Animated` de React Native para no agregar módulos nativos (las versiones instaladas se
 * actualizan por EAS Update).
 */
export function ProveedorMenuLateral({ children }: { children: ReactNode }) {
  const [visible, setVisible] = useState(false);
  const avance = useRef(new Animated.Value(0)).current;

  const abrir = useCallback(() => {
    setVisible(true);
    Animated.timing(avance, { toValue: 1, duration: DURACION, easing: Easing.out(Easing.cubic), useNativeDriver: true }).start();
  }, [avance]);

  const cerrar = useCallback(
    (despues?: () => void) => {
      Animated.timing(avance, { toValue: 0, duration: DURACION, easing: Easing.in(Easing.cubic), useNativeDriver: true }).start(() => {
        setVisible(false);
        despues?.();
      });
    },
    [avance],
  );

  const valor = useMemo(() => ({ abrir }), [abrir]);
  return (
    <ContextoMenu.Provider value={valor}>
      {children}
      <Modal visible={visible} transparent animationType="none" statusBarTranslucent navigationBarTranslucent onRequestClose={() => cerrar()}>
        <Panel avance={avance} cerrar={cerrar} />
      </Modal>
    </ContextoMenu.Provider>
  );
}

/** Botón ☰ del encabezado. */
export function BotonMenu() {
  const { abrir } = useMenuLateral();
  const c = useColores();
  return (
    <Pressable onPress={abrir} hitSlop={12} accessibilityRole="button" accessibilityLabel="Abrir menú" style={{ marginRight: 16 }}>
      <Icono nombre="menu" tamano={26} color={c.barraTexto} />
    </Pressable>
  );
}

interface Opcion {
  texto: string;
  icono: NombreIcono;
  ruta: string;
  href: Href;
  contador?: number;
  alerta?: boolean;
}

function Panel({ avance, cerrar }: { avance: Animated.Value; cerrar: (despues?: () => void) => void }) {
  const c = useColores();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();
  const ancho = Math.min(320, width * 0.82);
  const ruta = usePathname();
  const { perfil, salir } = useSesion();
  const { version, conRed } = useSync();

  const datos = useMemo(() => {
    const generico = clienteGenerico();
    return { estado: estadoDispositivo(), resumen: resumenDia(fechaOperativa()), canal: canalRuta(), generico };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [version]);

  const opciones: Opcion[] = [
    { texto: 'Mi día', icono: 'today', ruta: '/', href: '/' },
    { texto: 'Clientes de hoy', icono: 'groups', ruta: '/clientes', href: '/clientes' },
    ...(datos.canal?.pedidoPor === 'Ruta' && datos.generico
      ? [
          {
            texto: 'Pedido de la ruta',
            icono: 'grid-on' as const,
            ruta: `/pedido/${datos.generico.id}`,
            href: { pathname: '/pedido/[clienteId]', params: { clienteId: datos.generico.id, ruta: '1' } } as Href,
          },
        ]
      : []),
    { texto: 'Pedidos', icono: 'assignment', ruta: '/pedidos', href: '/pedidos', contador: datos.resumen.pedidosPorEnviar },
    { texto: 'Inventario de camioneta', icono: 'inventory', ruta: '/inventario', href: '/inventario' },
    {
      texto: 'Pendientes',
      icono: 'pending-actions',
      ruta: '/pendientes',
      href: '/pendientes',
      contador: datos.resumen.rechazadas || datos.resumen.pendientes,
      alerta: datos.resumen.rechazadas > 0,
    },
  ];

  // Cada opción abre su pantalla sobre "Mi día": atrás siempre regresa al inicio.
  const ir = (o: Opcion) =>
    cerrar(() => {
      if (o.ruta === ruta) return;
      if (router.canDismiss()) router.dismissAll();
      if (o.ruta !== '/') router.push(o.href);
    });

  const desplazamiento = avance.interpolate({ inputRange: [0, 1], outputRange: [-ancho, 0] });
  const opacidad = avance.interpolate({ inputRange: [0, 1], outputRange: [0, 1] });

  return (
    <View style={{ flex: 1 }}>
      <Animated.View style={{ position: 'absolute', top: 0, bottom: 0, left: 0, right: 0, backgroundColor: 'rgba(0,0,0,0.45)', opacity: opacidad }}>
        <Pressable style={{ flex: 1 }} onPress={() => cerrar()} accessibilityLabel="Cerrar menú" />
      </Animated.View>
      <Animated.View
        style={{
          position: 'absolute',
          top: 0,
          bottom: 0,
          left: 0,
          width: ancho,
          backgroundColor: c.tarjeta,
          transform: [{ translateX: desplazamiento }],
          borderTopRightRadius: 20,
          borderBottomRightRadius: 20,
          overflow: 'hidden',
        }}
      >
        <View style={{ backgroundColor: c.barra, paddingTop: insets.top + 20, paddingBottom: 20, paddingHorizontal: 20, gap: 12 }}>
          <View style={{ alignSelf: 'flex-start' }}>
            <Logo ancho={150} />
          </View>
          <View>
            <Text style={{ color: c.barraTexto, fontSize: 17, fontWeight: '600' }}>{perfil?.nombreCompleto ?? 'Chofer'}</Text>
            <Text style={{ color: c.barraTexto, opacity: 0.85, fontSize: 14 }}>
              {datos.estado.rutaNombre ? `${datos.estado.rutaNombre} · ${datos.estado.rutaCodigo}` : 'Sin ruta asignada'}
            </Text>
            <Text style={{ color: c.barraTexto, opacity: 0.85, fontSize: 13 }}>
              {conRed ? 'En línea' : 'Sin red'} · {datos.estado.codigo ?? 'sin dispositivo'}
            </Text>
          </View>
        </View>

        <ScrollView contentContainerStyle={{ paddingVertical: 12, paddingHorizontal: 12, gap: 4 }}>
          {opciones.map((o) => {
            const activa = o.ruta === ruta;
            return (
              <Pressable
                key={o.ruta}
                onPress={() => ir(o)}
                accessibilityRole="button"
                accessibilityState={{ selected: activa }}
                style={({ pressed }) => ({
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: 16,
                  paddingVertical: 14,
                  paddingHorizontal: 16,
                  borderRadius: RADIO_PILDORA,
                  backgroundColor: activa ? c.suave : pressed ? c.divisor : 'transparent',
                })}
              >
                <Icono nombre={o.icono} color={activa ? c.primario : c.textoSuave} />
                <Text style={{ flex: 1, fontSize: 16, color: activa ? c.primario : c.texto, fontWeight: activa ? '600' : '400' }}>{o.texto}</Text>
                {!!o.contador && <Contador valor={o.contador} alerta={o.alerta} />}
              </Pressable>
            );
          })}
        </ScrollView>

        <View style={{ borderTopWidth: 1, borderTopColor: c.divisor, padding: 12, paddingBottom: insets.bottom + 12 }}>
          <Pressable
            onPress={() => cerrar(() => void salir())}
            accessibilityRole="button"
            style={({ pressed }) => ({
              flexDirection: 'row',
              alignItems: 'center',
              gap: 16,
              paddingVertical: 14,
              paddingHorizontal: 16,
              borderRadius: RADIO_PILDORA,
              backgroundColor: pressed ? c.divisor : 'transparent',
            })}
          >
            <Icono nombre="logout" color={c.error} />
            <Text style={{ fontSize: 16, color: c.error }}>Cerrar sesión</Text>
          </Pressable>
        </View>
      </Animated.View>
    </View>
  );
}

function Contador({ valor, alerta }: { valor: number; alerta?: boolean }) {
  const c = useColores();
  const [fondo, tinta] = c.estado[alerta ? 'rojo' : 'amarillo'];
  return (
    <View style={{ minWidth: 26, paddingHorizontal: 8, paddingVertical: 2, borderRadius: 13, backgroundColor: fondo, alignItems: 'center' }}>
      <Text style={{ color: tinta, fontWeight: '700', fontSize: 13 }}>{valor}</Text>
    </View>
  );
}
