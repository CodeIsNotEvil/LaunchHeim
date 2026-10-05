plugins {
  alias(libs.plugins.android.application)
  alias(libs.plugins.kotlin.compose)
  alias(libs.plugins.kotlin.serialization)
}

// The companion ships with LaunchHeim and carries its version, so a launchheim-v* tag describes both.
// It's read from the desktop app's Directory.Build.props, the one place the release version is set.
val launchHeimProps: String = rootDir.resolve("../Directory.Build.props").readText()

fun launchHeimProperty(name: String): String =
  Regex("<$name>([^<]+)</$name>").find(launchHeimProps)?.groupValues?.get(1)?.trim()
    ?: error("No <$name> in ../Directory.Build.props")

val launchHeimVersion: String = launchHeimProperty("Version")

// 0.4.0 -> 400, 1.12.3 -> 11203. Android only needs it to grow with every release; F-Droid reads it
// from <AndroidVersionCode>, so a bump of <Version> without it stops here instead of on F-Droid.
val launchHeimVersionCode: Int = launchHeimVersion.split('.').map(String::toInt)
  .let { (major, minor, patch) -> major * 10_000 + minor * 100 + patch }
  .also { expected ->
    val written = launchHeimProperty("AndroidVersionCode").toInt()
    check(written == expected) { "<AndroidVersionCode> is $written, but <Version> $launchHeimVersion needs $expected. Update Directory.Build.props." }
  }

android {
  namespace = "local.cine.launchheim"
  compileSdk = 37

  defaultConfig {
    // The same reverse-domain id as the desktop app's AppStream metadata (io.github.codeisnotevil.LaunchHeim),
    // from the GitHub Pages domain the owner controls, which is what F-Droid expects of a package name.
    applicationId = "io.github.codeisnotevil.launchheim"
    // Android 8: adaptive icons, java.time and NIO without desugaring. Practically every phone in use.
    minSdk = 26
    // 36, not 37: Android 17 asks apps targeting it for a local network permission, which LocalSend
    // needs. That prompt and its denied path want testing on a real Android 17 phone first.
    targetSdk = 36
    versionName = launchHeimVersion
    versionCode = launchHeimVersionCode
  }

  buildTypes {
    release {
      isMinifyEnabled = true
      isShrinkResources = true
      proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
      // Unsigned: F-Droid builds the release from source and signs it with its own key.
    }
  }

  // AGP otherwise puts a dependency list into the APK, encrypted with a key only Google can read.
  // F-Droid refuses APKs carrying that blob.
  dependenciesInfo {
    includeInApk = false
    includeInBundle = false
  }

  buildFeatures {
    compose = true
  }

  compileOptions {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
  }

  testOptions {
    unitTests.isReturnDefaultValues = true
  }
}

// Bytecode for Java 17, built with whatever JDK runs Gradle (17 or newer). A toolchain would demand a JDK
// 17 install exactly, which F-Droid's build server or a machine with only 21 may not have.
kotlin {
  compilerOptions {
    jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
  }
}

dependencies {
  implementation(libs.androidx.core.ktx)
  implementation(libs.androidx.activity.compose)
  implementation(libs.androidx.lifecycle.viewmodel.compose)
  implementation(libs.androidx.lifecycle.runtime.compose)
  implementation(libs.androidx.lifecycle.process)
  implementation(libs.androidx.navigation.compose)
  implementation(platform(libs.compose.bom))
  implementation(libs.compose.ui)
  implementation(libs.compose.ui.tooling.preview)
  implementation(libs.compose.material3)
  implementation(libs.compose.material.icons)
  implementation(libs.kotlinx.serialization.json)
  implementation(libs.kotlinx.coroutines.android)
  implementation(libs.okhttp)
  implementation(libs.coil.compose)
  implementation(libs.coil.network.okhttp)
  debugImplementation(libs.compose.ui.tooling)

  testImplementation(libs.junit)
  testImplementation(libs.kotlinx.coroutines.test)
}
