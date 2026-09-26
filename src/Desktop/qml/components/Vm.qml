pragma Singleton
import QtQuick 2.15
import LaunchHeim 1.0

// Holds the view models and lists that QML binds to in JavaScript properties, and is the only way
// the other files reach them.
//
// Qml.Net hands every .NET object to QML with JavaScript ownership and a fresh wrapper per read. A
// wrapper that no JavaScript value refers to is deleted by the next garbage collection, and every
// binding that went through it silently stops updating (or reads null). `App.browse.details` in a
// binding is such a throwaway wrapper; `Vm.details` is held here and stays alive.
//
// Lists work the same way: a view converts its model to a QVariantList, which does not keep the
// wrappers alive, so the JS array from Net.toVariantList has to live in a `property var` like these.
// Net.toListModel is not used at all, since it creates a new wrapper for every item access.
QtObject {
  readonly property var browse: App.browse
  readonly property var settings: App.settings
  readonly property var prompt: App.browserPrompt
  readonly property var selected: App.selectedInstance
  readonly property var recent: App.recentInstance
  readonly property var details: browse.details

  readonly property var instances: Net.toVariantList(App.instanceItems)
  readonly property var activities: Net.toVariantList(App.activities)
  readonly property var toasts: Net.toVariantList(App.toasts)
  readonly property var results: Net.toVariantList(browse.results)
  readonly property var detailFiles: details ? Net.toVariantList(details.files) : []
}
