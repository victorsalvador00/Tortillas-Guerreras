import { Redirect } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, Text, View } from 'react-native';
import { useSesion } from '@/servicios/contextoSesion';
import { ErrorHttp } from '@/sync/puertos';
import { Logo } from '@/ui/Logo';
import { Aviso, Boton, Campo } from '@/ui/componentes';
import { useColores } from '@/ui/tema';

/** Inicio de sesión: la primera vez registra el teléfono (queda por aprobar). */
export default function Login() {
  const c = useColores();
  const { estado, entrar } = useSesion();
  const [usuario, setUsuario] = useState('');
  const [contrasena, setContrasena] = useState('');
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (estado === 'listo') return <Redirect href="/" />;
  if (estado === 'espera') return <Redirect href="/espera" />;

  const enviar = async () => {
    setError(null);
    if (!usuario.trim() || !contrasena) {
      setError('Escribe tu usuario y contraseña.');
      return;
    }
    setCargando(true);
    try {
      await entrar(usuario.trim(), contrasena);
    } catch (e) {
      setError(e instanceof ErrorHttp ? e.message : 'No se pudo iniciar sesión.');
    } finally {
      setCargando(false);
    }
  };

  return (
    <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1, backgroundColor: c.barra }}>
      <ScrollView contentContainerStyle={{ flexGrow: 1, justifyContent: 'center', padding: 24, gap: 24 }} keyboardShouldPersistTaps="handled">
        <View style={{ alignItems: 'center', gap: 12 }}>
          <Logo ancho={260} />
          <Text style={{ color: c.barraTexto, fontSize: 30, fontWeight: '700', letterSpacing: 1 }}>Bienvenido</Text>
          <Text style={{ color: c.barraTexto, opacity: 0.85, letterSpacing: 2 }}>App de ruta</Text>
        </View>
        <View style={{ backgroundColor: c.tarjeta, borderRadius: 14, padding: 20, gap: 16 }}>
          <Text style={{ color: c.texto, fontSize: 20, fontWeight: '600' }}>Iniciar sesión</Text>
          {error && <Aviso tipo="rojo">{error}</Aviso>}
          <Campo etiqueta="Usuario" autoCapitalize="none" autoCorrect={false} value={usuario} onChangeText={setUsuario} textContentType="username" />
          <Campo etiqueta="Contraseña" secureTextEntry value={contrasena} onChangeText={setContrasena} textContentType="password" onSubmitEditing={enviar} />
          <Boton texto="Iniciar sesión" grande cargando={cargando} alPresionar={enviar} />
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}
