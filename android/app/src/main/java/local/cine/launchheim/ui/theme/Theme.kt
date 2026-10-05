package local.cine.launchheim.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import local.cine.launchheim.R

/** LaunchHeim's orange, the same in light and dark, as on the desktop (Theme.qml). */
val Accent = Color(0xFFDE5833)
val Positive = Color(0xFF27AE60)

// Breeze's window and view colours, so the phone looks like the Plasma app it belongs to.
private val Dark = darkColorScheme(
  primary = Accent,
  onPrimary = Color.White,
  primaryContainer = Color(0xFF4A2418),
  onPrimaryContainer = Color(0xFFFFDBD0),
  secondary = Color(0xFFBDC3C7),
  secondaryContainer = Color(0xFF31363B),
  onSecondaryContainer = Color(0xFFFCFCFC),
  background = Color(0xFF1B1E20),
  onBackground = Color(0xFFFCFCFC),
  surface = Color(0xFF1B1E20),
  onSurface = Color(0xFFFCFCFC),
  surfaceVariant = Color(0xFF292C30),
  onSurfaceVariant = Color(0xFFA1A9B1),
  surfaceContainer = Color(0xFF232629),
  surfaceContainerHigh = Color(0xFF292C30),
  surfaceContainerHighest = Color(0xFF31363B),
  surfaceContainerLow = Color(0xFF202326),
  outline = Color(0xFF4D5257),
  outlineVariant = Color(0xFF3B4045),
  error = Color(0xFFDA4453),
)

private val Light = lightColorScheme(
  primary = Accent,
  onPrimary = Color.White,
  primaryContainer = Color(0xFFFFDBD0),
  onPrimaryContainer = Color(0xFF3A0B00),
  secondary = Color(0xFF4D5257),
  secondaryContainer = Color(0xFFDEE0E2),
  onSecondaryContainer = Color(0xFF232629),
  background = Color(0xFFFCFCFC),
  onBackground = Color(0xFF232629),
  surface = Color(0xFFFCFCFC),
  onSurface = Color(0xFF232629),
  surfaceVariant = Color(0xFFEFF0F1),
  onSurfaceVariant = Color(0xFF5D6166),
  surfaceContainer = Color(0xFFF2F3F4),
  surfaceContainerHigh = Color(0xFFEBECED),
  surfaceContainerHighest = Color(0xFFE3E5E7),
  surfaceContainerLow = Color(0xFFF7F7F8),
  outline = Color(0xFFBDC3C7),
  outlineVariant = Color(0xFFDEE0E2),
  error = Color(0xFFDA4453),
)

/** Kode Mono, the font of LaunchHeim's logo and desktop UI (SIL OFL 1.1, res/font). */
val KodeMono = FontFamily(
  Font(R.font.kode_mono_regular, FontWeight.Normal),
  Font(R.font.kode_mono_bold, FontWeight.Bold),
)

private fun Typography.withFamily(family: FontFamily): Typography {
  fun TextStyle.f() = copy(fontFamily = family)
  return copy(
    displayLarge = displayLarge.f(), displayMedium = displayMedium.f(), displaySmall = displaySmall.f(),
    headlineLarge = headlineLarge.f(), headlineMedium = headlineMedium.f(), headlineSmall = headlineSmall.f(),
    titleLarge = titleLarge.f().copy(fontWeight = FontWeight.Bold), titleMedium = titleMedium.f().copy(fontWeight = FontWeight.Bold),
    titleSmall = titleSmall.f().copy(fontWeight = FontWeight.Bold),
    bodyLarge = bodyLarge.f(), bodyMedium = bodyMedium.f(), bodySmall = bodySmall.f(),
    labelLarge = labelLarge.f(), labelMedium = labelMedium.f(), labelSmall = labelSmall.f(),
  )
}

@Composable
fun LaunchHeimTheme(content: @Composable () -> Unit) {
  MaterialTheme(
    colorScheme = if (isSystemInDarkTheme()) Dark else Light,
    typography = Typography().withFamily(KodeMono),
    content = content,
  )
}
