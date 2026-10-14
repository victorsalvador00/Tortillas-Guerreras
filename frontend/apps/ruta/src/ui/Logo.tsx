import { Image, View } from 'react-native';
import logo from '../../assets/marca/logo-guerreras.png';

/** Logotipo del cliente (Guerreras, por Familia Ochoa) con fondo transparente. */
export function Logo({ ancho = 220 }: { ancho?: number }) {
  return (
    <View style={{ alignSelf: 'center' }}>
      <Image source={logo} style={{ width: ancho, height: (ancho * 196) / 720 }} resizeMode="contain" accessibilityLabel="Guerreras, por Familia Ochoa" />
    </View>
  );
}
