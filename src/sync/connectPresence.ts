/**
 * Gram Connect — Multi-Device Presence & Session Casting (Spotify Connect style)
 * 
 * Manages connected devices (cli, desktop, mobile, web), heartbeats,
 * and allows 1-click casting of active sessions across machines.
 */

export type DeviceType = 'cli' | 'desktop' | 'web' | 'mobile' | 'cloud' | 'network';

export interface ConnectedDevice {
  deviceId: string;
  deviceName: string;
  deviceType: DeviceType;
  workspacePath?: string;
  activeSessionId?: string;
  isExecutionHost: boolean;
  lastSeenMs: number;
}

export interface SessionCastEvent {
  type: 'session_cast';
  fromDeviceId: string;
  targetDeviceId: string;
  sessionId: string;
  timestamp: string;
}

export class GramConnectManager {
  private devices = new Map<string, ConnectedDevice>();
  private staleTimeoutMs = 45_000;

  /**
   * Records or updates a device heartbeat.
   */
  public recordHeartbeat(params: {
    deviceId: string;
    deviceName?: string;
    deviceType?: DeviceType;
    workspacePath?: string;
    activeSessionId?: string;
    isExecutionHost?: boolean;
  }): ConnectedDevice {
    const now = Date.now();
    const existing = this.devices.get(params.deviceId);

    const updated: ConnectedDevice = {
      deviceId: params.deviceId,
      deviceName: params.deviceName || existing?.deviceName || params.deviceId,
      deviceType: params.deviceType || existing?.deviceType || 'cli',
      workspacePath: params.workspacePath || existing?.workspacePath,
      activeSessionId: params.activeSessionId || existing?.activeSessionId,
      isExecutionHost: params.isExecutionHost ?? existing?.isExecutionHost ?? false,
      lastSeenMs: now,
    };

    this.devices.set(params.deviceId, updated);
    return updated;
  }

  /**
   * Returns list of currently online devices.
   */
  public listOnlineDevices(): ConnectedDevice[] {
    const now = Date.now();
    const online: ConnectedDevice[] = [];

    for (const [id, dev] of this.devices.entries()) {
      if (now - dev.lastSeenMs <= this.staleTimeoutMs) {
        online.push(dev);
      } else {
        this.devices.delete(id);
      }
    }

    return online;
  }

  /**
   * Casts a session to another device (Spotify Connect style)
   */
  public castSession(fromDeviceId: string, targetDeviceId: string, sessionId: string): {
    success: boolean;
    event?: SessionCastEvent;
    error?: string;
  } {
    const target = this.devices.get(targetDeviceId);
    if (!target) {
      return { success: false, error: `Device '${targetDeviceId}' is offline or not found.` };
    }

    target.activeSessionId = sessionId;
    const event: SessionCastEvent = {
      type: 'session_cast',
      fromDeviceId,
      targetDeviceId,
      sessionId,
      timestamp: new Date().toISOString(),
    };

    return { success: true, event };
  }
}
