import type { ReactNode } from 'react';
import { Pressable, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Icono } from './componentes';
import { useColores } from './tema';

/** Lo que usa el encabezado de las opciones de la pantalla (subconjunto de `NativeStackHeaderProps`). */
interface PropsEncabezado {
  navigation: { goBack: () => void };
  route: { name: string };
  back?: unknown;
  options: {
    title?: string;
    headerTitle?: string | ((props: { children: string; tintColor?: string }) => ReactNode);
    headerLeft?: (props: { tintColor?: string; canGoBack: boolean }) => ReactNode;
    headerRight?: (props: { tintColor?: string; canGoBack: boolean }) => ReactNode;
  };
}

/**
 * Encabezado de las pantallas de trabajo. Con edge-to-edge el encabezado nativo de la pila anidada no siempre reserva
 * la barra de estado (la flecha y el título quedaban debajo y no recibían toques); este aplica el margen con el área
 * segura.
 */
export function Encabezado({ navigation, route, back, options }: PropsEncabezado) {
  const c = useColores();
  const { top } = useSafeAreaInsets();
  const titulo = typeof options.headerTitle === 'string' ? options.headerTitle : (options.title ?? route.name);
  const props = { tintColor: c.barraTexto, canGoBack: !!back };
  const izquierda = options.headerLeft
    ? options.headerLeft(props)
    : back
      ? (
          <Pressable
            onPress={() => navigation.goBack()}
            hitSlop={12}
            accessibilityRole="button"
            accessibilityLabel="Regresar"
            style={{ marginRight: 16 }}
          >
            <Icono nombre="arrow-back" tamano={24} color={c.barraTexto} />
          </Pressable>
        )
      : null;
  return (
    <View style={{ backgroundColor: c.barra, paddingTop: top }}>
      <View style={{ height: 56, flexDirection: 'row', alignItems: 'center', paddingHorizontal: 16 }}>
        {izquierda}
        <View style={{ flex: 1 }}>
          {typeof options.headerTitle === 'function' ? (
            options.headerTitle({ children: titulo, tintColor: c.barraTexto })
          ) : (
            <Text numberOfLines={1} style={{ color: c.barraTexto, fontSize: 20, fontWeight: '600' }}>
              {titulo}
            </Text>
          )}
        </View>
        {options.headerRight?.(props)}
      </View>
    </View>
  );
}
