plugins {
  alias(libs.plugins.android.application)
  alias(libs.plugins.kotlin.compose)
  alias(libs.plugins.kotlin.serialization)
}

// The companion ships with LaunchHeim and carries its version, so a launchheim-v* tag describes both.
// It's read from the desktop app's Directory.Build.props, the one place the release version is set.
val launchHeimVersion: String = Regex("<Version>([^<]+)</Version>")
  .find(rootDir.resolve("../Directory.Build.props").readText())
  ?.groupValues?.get(1)
  ?: error("No <Version> in ../Directory.Build.props")

// 0.4.0 -> 400, 1.12.3 -> 11203. Android only needs it to grow with every release.
val launchHeimVersionCode: Int = launchHeimVersion.split('.').map(String::toInt)
  .let { (major, minor, patch) -> major * 10_000 + minor * 100 + patch }

android {
  namespace = "local.cine.launchheim"
  compileSdk = 37

  defaultConfig {
    applicationId = "local.cine.launchheim"
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
      // Signed with the debug key unless a release key is configured, see README "Releases".
      signingConfig = signingConfigs.getByName("debug")
    }
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

kotlin {
  jvmToolchain(17)
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
