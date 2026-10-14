import { MaterialIcons } from '@expo/vector-icons';
import type { ComponentProps, ReactNode } from 'react';
import {
  ActivityIndicator,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
  type StyleProp,
  type TextInputProps,
  type TextStyle,
  type ViewStyle,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { RADIO_PILDORA, RADIO_TARJETA, useColores, type ColorEstado } from './tema';

export type NombreIcono = ComponentProps<typeof MaterialIcons>['name'];

export function Icono({ nombre, tamano = 24, color }: { nombre: NombreIcono; tamano?: number; color?: string }) {
  const c = useColores();
  return <MaterialIcons name={nombre} size={tamano} color={color ?? c.texto} />;
}

/** Contenedor de pantalla con fondo lavanda, desplazamiento y "jalar para sincronizar". */
export function Pantalla({
  children,
  alRefrescar,
  refrescando,
  pie,
}: {
  children: ReactNode;
  alRefrescar?: () => void;
  refrescando?: boolean;
  pie?: ReactNode;
}) {
  const c = useColores();
  return (
    <SafeAreaView edges={['bottom']} style={{ flex: 1, backgroundColor: c.fondo }}>
      <ScrollView
        contentContainerStyle={{ padding: 16, gap: 12, paddingBottom: pie ? 24 : 40 }}
        keyboardShouldPersistTaps="handled"
        refreshControl={alRefrescar ? <RefreshControl refreshing={!!refrescando} onRefresh={alRefrescar} /> : undefined}
      >
        {children}
      </ScrollView>
      {pie && (
        <View style={[estilos.pie, { backgroundColor: c.tarjeta, borderTopColor: c.divisor }]}>{pie}</View>
      )}
    </SafeAreaView>
  );
}

/** Tarjeta blanca radio 14 con sombra suave (en oscuro, contorno). */
export function Tarjeta({ children, titulo, accion, estilo, alPresionar }: {
  children?: ReactNode;
  titulo?: string;
  accion?: ReactNode;
  estilo?: StyleProp<ViewStyle>;
  alPresionar?: () => void;
}) {
  const c = useColores();
  const contenido = (
    <>
      {(titulo || accion) && (
        <View style={estilos.filaEntre}>
          {titulo ? <Text style={[estilos.tituloTarjeta, { color: c.texto }]}>{titulo}</Text> : <View />}
          {accion}
        </View>
      )}
      {children}
    </>
  );
  const base = [
    estilos.tarjeta,
    { backgroundColor: c.tarjeta, borderColor: c.borde, shadowColor: c.sombra },
    estilo,
  ];
  return alPresionar ? (
    <Pressable onPress={alPresionar} style={({ pressed }) => [...base, pressed && { opacity: 0.85 }]} android_ripple={{ color: c.suave }}>
      {contenido}
    </Pressable>
  ) : (
    <View style={base}>{contenido}</View>
  );
}

/** Botón en píldora: primario azul con sombra, secundario suave, peligro rojo. Grande para usar con una mano. */
export function Boton({
  texto,
  alPresionar,
  variante = 'primario',
  icono,
  cargando,
  deshabilitado,
  grande,
}: {
  texto: string;
  alPresionar: () => void;
  variante?: 'primario' | 'secundario' | 'peligro' | 'texto';
  icono?: NombreIcono;
  cargando?: boolean;
  deshabilitado?: boolean;
  grande?: boolean;
}) {
  const c = useColores();
  const fondo =
    variante === 'primario' ? c.primario : variante === 'secundario' ? c.suave : variante === 'peligro' ? c.estado.rojo[1] : 'transparent';
  const tinta = variante === 'primario' ? c.sobrePrimario : variante === 'peligro' ? '#fff' : c.primario;
  const inactivo = deshabilitado || cargando;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: inactivo, busy: cargando }}
      disabled={inactivo}
      onPress={alPresionar}
      style={({ pressed }) => [
        estilos.boton,
        {
          backgroundColor: fondo,
          minHeight: grande ? 58 : 48,
          opacity: inactivo ? 0.55 : pressed ? 0.85 : 1,
          shadowColor: variante === 'primario' ? c.primario : 'transparent',
          elevation: variante === 'primario' && !inactivo ? 4 : 0,
        },
      ]}
    >
      {cargando ? <ActivityIndicator color={tinta} /> : icono ? <MaterialIcons name={icono} size={22} color={tinta} /> : null}
      <Text style={[estilos.textoBoton, { color: tinta, fontSize: grande ? 17 : 15 }]}>{texto}</Text>
    </Pressable>
  );
}

const ETIQUETAS: Record<string, [string, ColorEstado]> = {
  Pendiente: ['Por enviar', 'gris'],
  Enviando: ['Enviando', 'azul'],
  Aplicada: ['Enviada', 'verde'],
  Rechazada: ['Rechazada', 'rojo'],
  Visitado: ['Visitado', 'verde'],
  NoVisitado: ['Sin visitar', 'gris'],
  Asignado: ['Asignado', 'azul'],
  Confirmado: ['Confirmado', 'azul'],
  EntregadoParcial: ['Parcial', 'amarillo'],
};

/** Etiqueta de estado en píldora (mismos colores que la web). */
export function Etiqueta({ estado, texto, color }: { estado?: string; texto?: string; color?: ColorEstado }) {
  const c = useColores();
  const [etq, col] = estado && ETIQUETAS[estado] ? ETIQUETAS[estado] : [texto ?? estado ?? '', color ?? 'gris'];
  const [fondo, tinta] = c.estado[color ?? col];
  return (
    <View style={[estilos.etiqueta, { backgroundColor: fondo }]}>
      <Text style={{ color: tinta, fontWeight: '600', fontSize: 13 }}>{texto ?? etq}</Text>
    </View>
  );
}

/** Campo de texto en píldora con etiqueta arriba. */
export function Campo({ etiqueta, error, ayuda, ...props }: TextInputProps & { etiqueta: string; error?: string; ayuda?: string }) {
  const c = useColores();
  return (
    <View style={{ gap: 6 }}>
      <Text style={{ color: c.textoSuave, fontSize: 13, fontWeight: '500', paddingHorizontal: 4 }}>{etiqueta}</Text>
      <TextInput
        placeholderTextColor={c.textoSuave}
        style={[
          estilos.campo,
          { backgroundColor: c.campo, borderColor: error ? c.error : c.campoBorde, color: c.texto },
        ]}
        {...props}
      />
      {error ? (
        <Text style={{ color: c.error, fontSize: 12, paddingHorizontal: 8 }}>{error}</Text>
      ) : ayuda ? (
        <Text style={{ color: c.textoSuave, fontSize: 12, paddingHorizontal: 8 }}>{ayuda}</Text>
      ) : null}
    </View>
  );
}

/** Grupo de opciones en píldora (forma de pago, condición, unidad…). */
export function Opciones<T extends string>({
  opciones,
  valor,
  alCambiar,
}: {
  opciones: { valor: T; texto: string }[];
  valor: T;
  alCambiar: (v: T) => void;
}) {
  const c = useColores();
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8 }}>
      {opciones.map((o) => {
        const activa = o.valor === valor;
        return (
          <Pressable
            key={o.valor}
            accessibilityRole="radio"
            accessibilityState={{ selected: activa }}
            onPress={() => alCambiar(o.valor)}
            style={[estilos.opcion, { backgroundColor: activa ? c.primario : c.suave }]}
          >
            <Text style={{ color: activa ? c.sobrePrimario : c.primario, fontWeight: '600' }}>{o.texto}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

/** Botones − cantidad + para capturar rápido con una mano. */
export function Cantidad({ valor, alCambiar, paso = 1, decimales = 0 }: { valor: number; alCambiar: (v: number) => void; paso?: number; decimales?: number }) {
  const c = useColores();
  const redondear = (v: number) => Math.max(0, Number(v.toFixed(decimales)));
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
      <Pressable accessibilityLabel="Menos" onPress={() => alCambiar(redondear(valor - paso))} style={[estilos.circulo, { backgroundColor: c.suave }]}>
        <MaterialIcons name="remove" size={24} color={c.primario} />
      </Pressable>
      <TextInput
        accessibilityLabel="Cantidad"
        keyboardType={decimales > 0 ? 'decimal-pad' : 'number-pad'}
        value={valor ? String(valor) : ''}
        placeholder="0"
        placeholderTextColor={c.textoSuave}
        onChangeText={(t) => {
          const n = Number(t.replace(',', '.'));
          if (Number.isFinite(n)) alCambiar(redondear(n));
        }}
        style={[estilos.cantidad, { color: c.texto, borderColor: c.campoBorde, backgroundColor: c.campo }]}
      />
      <Pressable accessibilityLabel="Más" onPress={() => alCambiar(redondear(valor + paso))} style={[estilos.circulo, { backgroundColor: c.primario }]}>
        <MaterialIcons name="add" size={24} color={c.sobrePrimario} />
      </Pressable>
    </View>
  );
}

export function Texto({ children, suave, negrita, tamano, estilo }: { children: ReactNode; suave?: boolean; negrita?: boolean; tamano?: number; estilo?: StyleProp<TextStyle> }) {
  const c = useColores();
  return (
    <Text style={[{ color: suave ? c.textoSuave : c.texto, fontWeight: negrita ? '600' : '400', fontSize: tamano ?? 15 }, estilo]}>
      {children}
    </Text>
  );
}

export function Aviso({ tipo = 'azul', children }: { tipo?: ColorEstado; children: ReactNode }) {
  const c = useColores();
  const [fondo, tinta] = c.estado[tipo];
  return (
    <View style={{ backgroundColor: fondo, borderRadius: 10, padding: 12 }}>
      <Text style={{ color: tinta }}>{children}</Text>
    </View>
  );
}

export function Vacio({ icono = 'inbox', texto }: { icono?: NombreIcono; texto: string }) {
  const c = useColores();
  return (
    <View style={{ alignItems: 'center', padding: 32, gap: 8 }}>
      <MaterialIcons name={icono} size={44} color={c.textoSuave} />
      <Text style={{ color: c.textoSuave, textAlign: 'center' }}>{texto}</Text>
    </View>
  );
}

/** Indicador grande del "Mi día" (ícono, número y título como en el tablero web). */
export function Indicador({ icono, valor, titulo, tono }: { icono: NombreIcono; valor: string; titulo: string; tono?: ColorEstado }) {
  const c = useColores();
  return (
    <View style={[estilos.tarjeta, estilos.indicador, { backgroundColor: c.tarjeta, borderColor: c.borde, shadowColor: c.sombra }]}>
      <View style={estilos.filaEntre}>
        <MaterialIcons name={icono} size={24} color={tono ? c.estado[tono][1] : c.texto} />
        <Text style={{ color: c.texto, fontSize: 24 }}>{valor}</Text>
      </View>
      <Text style={{ color: c.texto, fontWeight: '600', marginTop: 10 }}>{titulo}</Text>
    </View>
  );
}

export const estilos = StyleSheet.create({
  tarjeta: {
    borderRadius: RADIO_TARJETA,
    borderWidth: 1,
    padding: 16,
    gap: 10,
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 1,
    shadowRadius: 12,
    elevation: 2,
  },
  tituloTarjeta: { fontSize: 16, fontWeight: '600' },
  filaEntre: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  fila: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  boton: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingHorizontal: 24,
    borderRadius: RADIO_PILDORA,
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.32,
    shadowRadius: 12,
  },
  textoBoton: { fontWeight: '600', letterSpacing: 0.3 },
  etiqueta: { alignSelf: 'flex-start', borderRadius: 999, paddingHorizontal: 12, paddingVertical: 4 },
  campo: { borderWidth: 1, borderRadius: 24, minHeight: 52, paddingHorizontal: 18, fontSize: 16 },
  opcion: { borderRadius: 999, paddingHorizontal: 16, paddingVertical: 10, minHeight: 44, justifyContent: 'center' },
  circulo: { width: 48, height: 48, borderRadius: 24, alignItems: 'center', justifyContent: 'center' },
  cantidad: { width: 84, height: 48, borderWidth: 1, borderRadius: 12, textAlign: 'center', fontSize: 18 },
  pie: { padding: 16, borderTopWidth: 1, gap: 8 },
  indicador: { flex: 1, minWidth: 150 },
});
