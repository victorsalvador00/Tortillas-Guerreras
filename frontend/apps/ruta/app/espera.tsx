import { Redirect } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { Text, View } from 'react-native';
import { useSesion } from '@/servicios/contextoSesion';
import { Logo } from '@/ui/Logo';
import { Aviso, Boton, Icono } from '@/ui/componentes';
import { useColores } from '@/ui/tema';

/** El teléfono quedó registrado y espera la aprobación de un administrador o supervisor (RN-SEG-03). */
export default function Espera() {
  const c = useColores();
  const { estado, revisar, salir } = useSesion();
  const [revisando, setRevisando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const verificar = useCallback(async () => {
    setRevisando(true);
    setError(null);
    try {
      await revisar();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo revisar.');
    } finally {
      setRevisando(false);
    }
  }, [revisar]);

  useEffect(() => {
    const t = setInterval(() => void verificar(), 15_000);
    return () => clearInterval(t);
  }, [verificar]);

  if (estado === 'listo') return <Redirect href="/" />;
  if (estado === 'sinSesion') return <Redirect href="/login" />;

  return (
    <View style={{ flex: 1, backgroundColor: c.fondo, justifyContent: 'center', padding: 24, gap: 20 }}>
      <Logo ancho={220} />
      <View style={{ backgroundColor: c.tarjeta, borderRadius: 14, padding: 20, gap: 14, alignItems: 'center' }}>
        <Icono nombre="hourglass-top" tamano={48} color={c.primario} />
        <Text style={{ color: c.texto, fontSize: 20, fontWeight: '600', textAlign: 'center' }}>En espera de aprobación</Text>
        <Text style={{ color: c.textoSuave, textAlign: 'center' }}>
          Este teléfono quedó registrado. Pide a un administrador o supervisor que lo apruebe en la web (Seguridad › Dispositivos).
          Revisamos cada 15 segundos.
        </Text>
        {error && <Aviso tipo="rojo">{error}</Aviso>}
        <Boton texto="Revisar ahora" icono="refresh" cargando={revisando} alPresionar={verificar} />
        <Boton texto="Usar otro usuario" variante="texto" alPresionar={salir} />
      </View>
    </View>
  );
}
