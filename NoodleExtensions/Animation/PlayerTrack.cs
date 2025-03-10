using System.Collections.Generic;
using CustomJSONData;
using CustomJSONData.CustomBeatmap;
using Heck;
using Heck.Animation;
using Heck.Animation.Transform;
using Heck.Deserialize;
using Heck.Event;
using JetBrains.Annotations;
using NoodleExtensions.Managers;
using UnityEngine;
using Zenject;
using static Heck.HeckController;
using static NoodleExtensions.NoodleController;

namespace NoodleExtensions.Animation;

internal class PlayerTrack : MonoBehaviour
{
    // because camera2 is cringe
    // stop using reflection you jerk
    [UsedImplicitly]
    private static PlayerTrack? _instance;

    private bool _leftHanded;
    private MultiplayerOutroAnimationController? _multiOutroController;

    private GameObject _multiplayerPositioner = null!;
    private MultiplayerPlayersManager? _multiPlayersManager;
    private PauseController? _pauseController;
    private Quaternion _startLocalRot = Quaternion.identity;

    private Vector3 _startPos = Vector3.zero;

    private PlayerObject _target;

    private Track? _track;

    private TransformController? _transformController;
    private TransformControllerFactory _transformFactory = null!;
    private bool _v2;

    internal void AssignTrack(
        Track track)
    {
        _track?.RemoveGameObject(gameObject);

        _track = track;

        track.AddGameObject(gameObject);

        if (_v2)
        {
            return;
        }

        _transformController = _transformFactory.Create(gameObject, _track, true);
    }

    [UsedImplicitly]
    [Inject]
    private void Construct(
        IReadonlyBeatmapData beatmapData,
        [Inject(Id = LEFT_HANDED_ID)] bool leftHanded,
        TransformControllerFactory transformControllerFactory,
        [InjectOptional] PauseController? pauseController,
        [InjectOptional] PauseMenuManager? pauseMenuManager,
        [InjectOptional] MultiplayerLocalActivePlayerInGameMenuController? multiMenuController,
        [InjectOptional] MultiplayerPlayersManager? multiPlayersManager,
        [InjectOptional] MultiplayerOutroAnimationController? multiOutroController,
        PlayerObject target)
    {
        if (pauseController != null)
        {
            pauseController.didPauseEvent += OnDidPauseEvent;
            pauseController.didResumeEvent += OnDidResumeEvent;
        }

        Transform origin = transform;
        _startLocalRot = origin.localRotation;
        _startPos = origin.localPosition;
        _leftHanded = leftHanded;
        _transformFactory = transformControllerFactory;
        _target = target;

        if (target == PlayerObject.Root)
        {
            _pauseController = pauseController;
            _multiPlayersManager = multiPlayersManager;
            _multiOutroController = multiOutroController;

            if (pauseMenuManager != null)
            {
                pauseMenuManager.transform.SetParent(origin, false);
            }

            if (multiMenuController != null)
            {
                multiMenuController.transform.SetParent(origin, false);
            }
        }

        // v3 uses an underlying TransformController
        _v2 = ((CustomBeatmapData)beatmapData).version.IsVersion2();
        if (!_v2)
        {
            enabled = false;
        }

        // cam2 is cringe cam2 is cringe cam2 is cringe
        // ReSharper disable once InvertIf
        switch (target)
        {
            case PlayerObject.Head:
                _instance = this;

                GameObject headCam2Dummy = new("NoodlePlayerTrackHead");
                headCam2Dummy.transform.SetParent(transform);
                headCam2Dummy.AddComponent<MirrorParentTransform>();
                break;

            case PlayerObject.Root:
                _instance ??= this;

                // cam2 is insanely cringe and will not track properly when both a "NoodlePlayerTrackHead"
                // and a "NoodlePlayerTrackRoot" exist, as it only looks for localPosition of head,
                // so it will never track root properly
                // my stupid solution: create a dummy object named "NoodlePlayerTrackRoot"
                // whose local position mirrors when I set
                GameObject rootCam2Dummy = new("NoodlePlayerTrackRoot");
                rootCam2Dummy.transform.SetParent(transform);
                rootCam2Dummy.AddComponent<MirrorParentTransform>();
                break;
        }
    }

    private void OnDestroy()
    {
        if (_pauseController != null)
        {
            _pauseController.didPauseEvent -= OnDidPauseEvent;
        }

        if (_transformController != null)
        {
            Destroy(_transformController);
        }

        // cam2 is cringe cam2 is cringe cam2 is cringe
        _instance = null;
    }

    private void OnDidPauseEvent()
    {
        if (_target != PlayerObject.Root)
        {
            Transform transform1 = transform;
            transform1.localPosition = _startPos;
            transform1.localRotation = _startLocalRot;
        }

        if (_v2)
        {
            enabled = false;
        }

        if (_transformController != null)
        {
            _transformController.enabled = false;
        }
    }

    private void OnDidResumeEvent()
    {
        if (_v2)
        {
            enabled = true;
        }

        if (_transformController != null)
        {
            _transformController.enabled = true;
        }
    }

    private void Start()
    {
        if (_multiPlayersManager == null)
        {
            return;
        }

        _multiplayerPositioner = new GameObject();
        _multiplayerPositioner.transform.SetParent(transform);
    }

    private void Update()
    {
        if (_track == null)
        {
            return;
        }

        Quaternion? rotation = _track.GetProperty<Quaternion>(OFFSET_ROTATION)?.Mirror(_leftHanded);
        Vector3? position = _track.GetProperty<Vector3>(OFFSET_POSITION)?.Mirror(_leftHanded);

        Quaternion worldRotationQuaternion = Quaternion.identity;
        Vector3 positionVector = _startPos;
        if (rotation.HasValue || position.HasValue)
        {
            Quaternion finalRot = rotation ?? Quaternion.identity;
            worldRotationQuaternion *= finalRot;
            Vector3 finalPos = position ?? Vector3.zero;
            positionVector = worldRotationQuaternion *
                             ((finalPos * StaticBeatmapObjectSpawnMovementData.kNoteLinesDistance) + _startPos);
        }

        worldRotationQuaternion *= _startLocalRot;
        Quaternion? localRotation = _track.GetProperty<Quaternion>(LOCAL_ROTATION)?.Mirror(_leftHanded);
        if (localRotation.HasValue)
        {
            worldRotationQuaternion *= localRotation.Value;
        }

        Transform transform1 = transform;
        transform1.localRotation = worldRotationQuaternion;
        transform1.localPosition = positionVector;

        if (_multiPlayersManager != null)
        {
            foreach (IConnectedPlayer player in _multiPlayersManager.allActiveAtGameStartPlayers)
            {
                if (!_multiPlayersManager.TryGetConnectedPlayerController(
                        player.userId,
                        out MultiplayerConnectedPlayerFacade connectedPlayerController))
                {
                    continue;
                }

                _multiplayerPositioner.transform.localPosition = connectedPlayerController.transform.position;
                Transform avatar = connectedPlayerController.transform.Find("MultiplayerGameAvatar");
                avatar.position = _multiplayerPositioner.transform.position;
                avatar.rotation = _multiplayerPositioner.transform.rotation;
            }
        }

        if (_multiOutroController == null)
        {
            return;
        }

        Transform transform2 = _multiOutroController.transform;
        transform2.position = transform1.position;
        transform2.rotation = transform1.rotation;
    }
}

[CustomEvent(ASSIGN_PLAYER_TO_TRACK)]
internal class AssignPlayerToTrack : ICustomEvent
{
    private readonly IInstantiator _instantiator;
    private readonly NoodlePlayerTransformManager _noodlePlayerTransformManager;
    private readonly DeserializedData _deserializedData;
    private readonly Dictionary<PlayerObject, PlayerTrack> _playerTracks = new();

    private AssignPlayerToTrack(
        IInstantiator instantiator,
        NoodlePlayerTransformManager noodlePlayerTransformManager,
        [Inject(Id = ID)] DeserializedData deserializedData)
    {
        _instantiator = instantiator;
        _noodlePlayerTransformManager = noodlePlayerTransformManager;
        _deserializedData = deserializedData;
    }

    public void Callback(CustomEventData customEventData)
    {
        if (!_deserializedData.Resolve(customEventData, out NoodlePlayerTrackEventData? noodlePlayerData))
        {
            return;
        }

        PlayerObject playerTrackObject = noodlePlayerData.PlayerObject;
        if (!_playerTracks.TryGetValue(playerTrackObject, out PlayerTrack? playerTrack))
        {
            _playerTracks[playerTrackObject] = playerTrack = _instantiator.InstantiateComponent<PlayerTrack>(
                _noodlePlayerTransformManager.GetByPlayerObject(playerTrackObject),
                [playerTrackObject]);
        }

        playerTrack.AssignTrack(noodlePlayerData.Track);
    }
}
