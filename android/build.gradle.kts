// AGP 9 compiles Kotlin itself ("built-in Kotlin"), so there is no org.jetbrains.kotlin.android plugin.
// Declaring the Compose and serialization compiler plugins here also puts their Kotlin Gradle plugin on
// the build classpath, which lifts AGP's bundled Kotlin (2.2) to the version in libs.versions.toml.
plugins {
  alias(libs.plugins.android.application) apply false
  alias(libs.plugins.kotlin.compose) apply false
  alias(libs.plugins.kotlin.serialization) apply false
}
