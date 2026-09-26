// Replacement for net_instance_activateSignal from Qml.Net 0.11 (qmlnet-native, types/NetReference.cpp).
//
// Qml.Net wraps a .NET object in a new NetValue QObject every time QML reads it, so one object usually
// has several live wrappers: one per binding, list delegate or property read. The original raises a
// signal like this:
//
//     result = result || liveInstance->activateSignal(signalName, parameters);
//
// `||` short-circuits, so once the first wrapper has handled the signal the others are never told.
// A property change then only updates whichever binding happens to hold the oldest wrapper, and the
// rest of the UI shows stale values. This version notifies every live wrapper.
//
// It does not link against libQmlNet.so. C# looks up the two NetValue functions it needs and passes
// them to launchheim_signalfix_init, which keeps this library independent of where Qml.Net was loaded
// from.

#include <QtCore/QList>
#include <QtCore/QSharedPointer>
#include <QtCore/QString>

class NetReference;
class NetVariantList;
class NetValue;

// Layouts copied from qmlnet-native (types/NetReference.h, qml/NetVariantList.h).
struct NetReferenceContainer {
  QSharedPointer<NetReference> instance;
};

struct NetVariantListContainer {
  QSharedPointer<NetVariantList> list;
};

// NetValue::getAllLiveInstances is static; NetValue::activateSignal is a non-virtual member, which the
// Itanium C++ ABI calls like a free function taking `this` first.
using GetAllLiveInstancesFn = QList<NetValue*> (*)(const QSharedPointer<NetReference>&);
using ActivateSignalFn = bool (*)(NetValue*, const QString&, const QSharedPointer<NetVariantList>&);

static GetAllLiveInstancesFn getAllLiveInstances = nullptr;
static ActivateSignalFn activateSignal = nullptr;

extern "C" {

__attribute__((visibility("default"))) void launchheim_signalfix_init(void* getAllLiveInstancesPtr, void* activateSignalPtr) {
  getAllLiveInstances = reinterpret_cast<GetAllLiveInstancesFn>(getAllLiveInstancesPtr);
  activateSignal = reinterpret_cast<ActivateSignalFn>(activateSignalPtr);
}

__attribute__((visibility("default"))) unsigned char launchheim_activate_signal(
  NetReferenceContainer* container,
  const QChar* signalName,
  NetVariantListContainer* parametersContainer) {
  const QList<NetValue*> liveInstances = getAllLiveInstances(container->instance);
  if (liveInstances.isEmpty()) {
    // Not alive in the QML world, so no signals to raise.
    return 0;
  }

  const QString signalNameString(signalName);
  QSharedPointer<NetVariantList> parameters;
  if (parametersContainer != nullptr) {
    parameters = parametersContainer->list;
  }

  bool result = false;
  for (NetValue* liveInstance : liveInstances) {
    // Handling a signal re-runs bindings, and those can drop other wrappers. Only touch the ones
    // that are still alive.
    if (!getAllLiveInstances(container->instance).contains(liveInstance)) {
      continue;
    }

    result = activateSignal(liveInstance, signalNameString, parameters) || result;
  }

  return result ? 1 : 0;
}

}
