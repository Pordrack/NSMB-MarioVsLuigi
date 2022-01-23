﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using TMPro;

public class GameManager : MonoBehaviour, IOnEventCallback {
    public static GameManager Instance { get; private set; }

    public Sprite destroyedPipeSprite;
    [SerializeField] AudioClip intro, loop, invincibleIntro, invincibleLoop, megaMushroomLoop;

    public int levelMinTileX, levelMinTileY, levelWidthTile, levelHeightTile;
    public Vector3 spawnpoint;
    public Tilemap tilemap;
    public bool canSpawnMegaMushroom = true;
    TileBase[] originalTiles;
    BoundsInt origin;
    GameObject currentStar = null;
    GameObject[] starSpawns;
    List<GameObject> remainingSpawns = new List<GameObject>();
    float spawnStarCount;
    public AudioSource music, sfx;

    public GameObject localPlayer;
    public bool paused, loaded;
    [SerializeField] GameObject pauseUI, pauseButton;
    public bool gameover = false, musicEnabled = false;
    public List<string> loadedPlayers = new List<string>();
    public int starRequirement;

    public PlayerController[] allPlayers;

    // EVENT CALLBACK
    public void OnEvent(EventData e) {
        OnEvent(e.Code, e.CustomData);
    }
    public void OnEvent(byte eventId, object customData) {
        switch (eventId) {
        case (byte) Enums.NetEventIds.EndGame: {
            Player winner = (Player) customData;
            StartCoroutine(EndGame(winner));
            break;
        }
        case (byte) Enums.NetEventIds.SetTile: {
            object[] data = (object[]) customData;
            int x = (int) data[0];
            int y = (int) data[1];
            string tilename = (string) data[2];
            Vector3Int loc = new Vector3Int(x,y,0);
            tilemap.SetTile(loc, (Tile) Resources.Load("Tilemaps/Tiles/" + tilename));
            break;
        }
        case (byte) Enums.NetEventIds.SetTileBatch: {
            object[] data = (object[]) customData;
            int x = (int) data[0];
            int y = (int) data[1];
            int width = (int) data[2];
            int height = (int) data[3];
            string[] tiles = (string[]) data[4];
            TileBase[] tileObjects = new TileBase[tiles.Length];
            for (int i = 0; i < tiles.Length; i++) {
                string tile = tiles[i];
                if (tile == "") continue;
                tileObjects[i] = (TileBase) Resources.Load("Tilemaps/Tiles/" + tile);
            }
            tilemap.SetTilesBlock(new BoundsInt(x, y, 0, width, height, 1), tileObjects);
            break;
        }
        case (byte) Enums.NetEventIds.PlayerFinishedLoading: {
            Player player = (Player) customData;
            loadedPlayers.Add(player.NickName);
            break;
        }
        case (byte) Enums.NetEventIds.BumpTile: {
            object[] data = (object[]) customData;
            int x = (int) data[0];
            int y = (int) data[1];
            bool downwards = (bool) data[2];
            string newTile = (string) data[3];
            BlockBump.SpawnResult spawnResult = (BlockBump.SpawnResult) data[4]; 
            
            Vector3Int loc = new Vector3Int(x,y,0);

            GameObject bump = (GameObject) GameObject.Instantiate(Resources.Load("Prefabs/Bump/BlockBump"), Utils.TilemapToWorldPosition(loc) + new Vector3(0.25f, 0.25f), Quaternion.identity);
            BlockBump bb = bump.GetComponentInChildren<BlockBump>();

            bb.fromAbove = downwards;
            bb.resultTile = newTile;
            bb.sprite = tilemap.GetSprite(loc);
            bb.spawn = spawnResult;

            tilemap.SetTile(loc, null);
            break;
        }
        case (byte) Enums.NetEventIds.SpawnParticle: {
            object[] data = (object[]) customData;
            int x = (int) data[0];
            int y = (int) data[1];
            string particleName = (string) data[2];
            Vector3 color = (data.Length > 3 ? (Vector3) data[3] : new Vector3(1,1,1));

            Vector3Int loc = new Vector3Int(x,y,0);

            GameObject particle = (GameObject) GameObject.Instantiate(Resources.Load("Prefabs/Particle/" + particleName), Utils.TilemapToWorldPosition(loc) + new Vector3(0.25f, 0.25f), Quaternion.identity);
            ParticleSystem system = particle.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.startColor = new Color(color.x, color.y, color.z, 1);
            break;
        }
        case (byte) Enums.NetEventIds.SpawnDestructablePipe: {
            object[] data = (object[]) customData;
            float x = (float) data[0];
            float y = (float) data[1];
            bool right = (bool) data[2];
            bool upsideDown = (bool) data[3];
            int tiles = (int) data[4];
            bool alreadyDestroyed = (bool) data[5];
            GameObject particle = (GameObject) GameObject.Instantiate(Resources.Load("Prefabs/Particle/DestructablePipe"), new Vector2(x + (right ? 0.5f : 0f), y + (tiles/4f * (upsideDown ? -1 : 1))), Quaternion.Euler(0, 0, upsideDown ? 180 : 0));
            Rigidbody2D body = particle.GetComponent<Rigidbody2D>();
            body.velocity = new Vector2(right ? 7 : -7, 4);
            body.angularVelocity = (right ^ upsideDown ? -300 : 300); 
            SpriteRenderer sr = particle.GetComponent<SpriteRenderer>();
            sr.size = new Vector2(2, tiles);
            if (alreadyDestroyed)
                sr.sprite = destroyedPipeSprite;
            //TODO: find the right sound
            sfx.PlayOneShot((AudioClip) Resources.Load("Sound/player/brick_break"));
            break;
        }
        }
    }

    void OnEnable() {
        PhotonNetwork.AddCallbackTarget(this);
    }
    void OnDisable() {
        PhotonNetwork.RemoveCallbackTarget(this);
    }


    void Start() {
        Instance = this;
        
        origin = new BoundsInt(levelMinTileX, levelMinTileY, 0, levelWidthTile, levelHeightTile, 1);
        starSpawns = GameObject.FindGameObjectsWithTag("StarSpawn");
        starRequirement = (int) PhotonNetwork.CurrentRoom.CustomProperties[Enums.NetRoomProperties.StarRequirement];

        TileBase[] map = tilemap.GetTilesBlock(origin);
        originalTiles = new TileBase[map.Length];
        for (int i = 0; i < map.Length; i++) {
            if (map[i] == null) continue;
            originalTiles[i] = map[i];
        }

        
        SceneManager.SetActiveScene(gameObject.scene);
        localPlayer = PhotonNetwork.Instantiate("Prefabs/" + Utils.GetCharacterData().prefab, spawnpoint, Quaternion.identity, 0);
        if (!localPlayer) {
            //not connected to a room, started scene through editor. spawn player
            PhotonNetwork.OfflineMode = true;
            PhotonNetwork.CreateRoom("debug");
            GameObject.Instantiate(Resources.Load("Prefabs/Static/GlobalController"), Vector3.zero, Quaternion.identity);
            localPlayer = PhotonNetwork.Instantiate("Prefabs/PlayerMario", spawnpoint, Quaternion.identity, 0);
        }
        Camera.main.GetComponent<CameraController>().target = localPlayer;
        localPlayer.GetComponent<Rigidbody2D>().isKinematic = true;
        localPlayer.GetComponent<PlayerController>().enabled = false;

        PhotonNetwork.SerializationRate = 50;

        PhotonNetwork.IsMessageQueueRunning = true;

        RaiseEventOptions options = new RaiseEventOptions {Receivers=ReceiverGroup.Others, CachingOption=EventCaching.AddToRoomCache};
        PhotonNetwork.RaiseEvent((byte) Enums.NetEventIds.PlayerFinishedLoading, PhotonNetwork.LocalPlayer, options, SendOptions.SendReliable);
        loadedPlayers.Add(PhotonNetwork.LocalPlayer.NickName);
    }
    public void LoadingComplete() {
        loaded = true;
        loadedPlayers.Clear();
        allPlayers = FindObjectsOfType<PlayerController>();
        if (PhotonNetwork.IsMasterClient) {
            //clear buffered loading complete events. 
            RaiseEventOptions options = new RaiseEventOptions {Receivers=ReceiverGroup.MasterClient, CachingOption=EventCaching.RemoveFromRoomCache};
            PhotonNetwork.RaiseEvent((byte) Enums.NetEventIds.PlayerFinishedLoading, null, options, SendOptions.SendReliable);
        }

        GameObject canvas = GameObject.FindGameObjectWithTag("LoadingCanvas");
        if (canvas) {
            canvas.GetComponent<Animator>().SetTrigger("loaded");
            canvas.GetComponent<AudioSource>().Stop();
        }
        StartCoroutine(WaitToActivate());
    }

    IEnumerator WaitToActivate() {
        yield return new WaitForSeconds(3.5f);
        music.PlayOneShot((AudioClip) Resources.Load("Sound/startgame")); 

        foreach (var wfgs in GameObject.FindObjectsOfType<WaitForGameStart>()) {
            wfgs.AttemptExecute();
        }
        if (PhotonNetwork.IsMasterClient) {
            foreach (EnemySpawnpoint point in GameObject.FindObjectsOfType<EnemySpawnpoint>()) {
                point.AttemptSpawning();
            }
        }
        localPlayer.GetComponent<Rigidbody2D>().isKinematic = false;
        localPlayer.GetComponent<PlayerController>().enabled = true;
        localPlayer.GetPhotonView().RPC("PreRespawn", RpcTarget.All);

        yield return new WaitForSeconds(1f);
        musicEnabled = true;
        SceneManager.UnloadSceneAsync("Loading");
    }

    IEnumerator EndGame(Photon.Realtime.Player winner) {
        gameover = true;
        music.Stop();
        GameObject text = GameObject.FindWithTag("wintext");
        text.GetComponent<TMP_Text>().text = winner.NickName + " Wins!";
        yield return new WaitForSecondsRealtime(1);
        text.GetComponent<Animator>().SetTrigger("start");

        AudioMixer mixer = music.outputAudioMixerGroup.audioMixer;
        mixer.SetFloat("MusicSpeed", 1f);
        mixer.SetFloat("MusicPitch", 1f);
        if (winner.IsLocal) {
            music.PlayOneShot((AudioClip) Resources.Load("Sound/match-win"));
        } else {
            music.PlayOneShot((AudioClip) Resources.Load("Sound/match-lose"));
        }
        //TOOD: make a results screen?

        yield return new WaitForSecondsRealtime(4);
        SceneManager.LoadScene("MainMenu");
    }

    void Update() {
        if (gameover) return;

        if (PhotonNetwork.IsMasterClient) {
            foreach (var player in allPlayers) {
                if (player.stars >= starRequirement) {
                    //game over, losers
                    
                    RaiseEventOptions options = new RaiseEventOptions {Receivers=ReceiverGroup.All};
                    PhotonNetwork.RaiseEvent((byte) Enums.NetEventIds.EndGame, player.photonView.Owner, options, SendOptions.SendReliable);
                    return;
                }
            }

        }

        if (!loaded) {
            if (PhotonNetwork.CurrentRoom == null || loadedPlayers.Count >= PhotonNetwork.CurrentRoom.PlayerCount) {
                LoadingComplete();
            }
            return;
        }
        if (musicEnabled) {
            HandleMusic();
        }


        if (currentStar == null) {
            if (PhotonNetwork.IsMasterClient) {
                if ((spawnStarCount -= Time.deltaTime) <= 0) {
                    if (remainingSpawns.Count == 0) {
                        remainingSpawns.AddRange(starSpawns);
                    }
                    int index = (int) (Random.value * remainingSpawns.Count);
                    Vector3 spawnPos = remainingSpawns[index].transform.position;
                    //Check for people camping spawn
                    foreach (var hit in Physics2D.OverlapCircleAll(spawnPos, 4)) {
                        if (hit.gameObject.tag == "Player") {
                            //cant spawn here
                            return;
                        }
                    }

                    currentStar = PhotonNetwork.InstantiateRoomObject("Prefabs/BigStar", spawnPos, Quaternion.identity);
                    remainingSpawns.RemoveAt(index);
                    //TODO: star appear sound
                    spawnStarCount = 15f;
                }
            } else {
                currentStar = GameObject.FindGameObjectWithTag("bigstar");
            }
        }
    }

    void HandleMusic() {
        if (intro != null) {
            intro = null;
            music.clip = intro;
            music.loop = false;
            music.Play();
        }

        bool invincible = false;
        bool mega = false;
        bool speedup = false;
        
        foreach (var player in allPlayers) {
            if (player == null) return;
            if (player.state == Enums.PowerupState.Giant && player.giantStartTimer <= 0) {
                mega = true;
            }
            if (player.invincible > 0) {
                invincible = true;
            }
            int stars = player.stars;
            if (((float) stars + 1) / starRequirement >= 0.95f) {
                speedup = true;
            }
        }

        if (mega) {
            if (music.clip != megaMushroomLoop || !music.isPlaying) {
                music.clip = megaMushroomLoop;
                music.loop = true;
                music.Play();
            }
        } else if (invincible) {
            if (music.clip == intro || music.clip == loop) {
                music.clip = invincibleIntro;
                music.loop = false;
                music.Play();
            }
            if (music.clip == invincibleIntro && !music.isPlaying) {
                music.clip = invincibleLoop;
                music.loop = true;
                music.Play();
            }
            return;
        } else if (!(music.clip == intro || music.clip == loop)) {
            music.Stop();
            if (intro != null) {
                music.clip = intro;
                music.loop = false;
                music.Play();
            }
        }
        if (!music.isPlaying) {
            music.clip = loop;
            music.loop = true;
            music.Play();
        }

        AudioMixer mixer = music.outputAudioMixerGroup.audioMixer;
        if (speedup) {
            mixer.SetFloat("MusicSpeed", 1.25f);
            mixer.SetFloat("MusicPitch", 1f / 1.25f);
        } else {
            mixer.SetFloat("MusicSpeed", 1f);
            mixer.SetFloat("MusicPitch", 1f);
        }
    }

    public void ResetTiles() {
        foreach (GameObject coin in GameObject.FindGameObjectsWithTag("coin")) {
            coin.GetComponent<SpriteRenderer>().enabled = true;
            coin.GetComponent<BoxCollider2D>().enabled = true;
        }

        tilemap.SetTilesBlock(origin, originalTiles);
        if (PhotonNetwork.IsMasterClient) {
            foreach (EnemySpawnpoint point in GameObject.FindObjectsOfType<EnemySpawnpoint>()) {
                point.AttemptSpawning();
            }
        }
    }

    public void Pause() {
        paused = !paused;
        Instance.pauseUI.SetActive(paused);
        EventSystem.current.SetSelectedGameObject(pauseButton);
    }

    public void Quit() {
        PhotonNetwork.LeaveRoom();
        SceneManager.LoadScene("MainMenu");
    }

    public float GetLevelMinX() {
        return (levelMinTileX * tilemap.transform.localScale.x) + tilemap.transform.position.x;
    }
    public float GetLevelMinY() {
        return (levelMinTileY * tilemap.transform.localScale.y) + tilemap.transform.position.y;
    }
    public float GetLevelMaxX() {
        return ((levelMinTileX + levelWidthTile) * tilemap.transform.localScale.x) + tilemap.transform.position.x;
    }
    public float GetLevelMaxY() {
        return ((levelMinTileY + levelHeightTile) * tilemap.transform.localScale.y) + tilemap.transform.position.y;
    }


    public float size = 1.39f, ySize = 0.8f;
    public Vector3 GetSpawnpoint(int playerIndex, int players = -1) {
        if (players <= -1)
            players = PhotonNetwork.CurrentRoom.PlayerCount;
        float comp = ((float) playerIndex/players) * 2 * Mathf.PI + (Mathf.PI/2f) + (Mathf.PI/(2*players));
        float scale = (2-(players+1f)/players) * size;
        Vector3 spawn = spawnpoint + new Vector3(Mathf.Sin(comp) * scale, Mathf.Cos(comp) * (players > 2 ? scale * ySize : 0), 0);
        if (spawn.x < GetLevelMinX()) spawn += new Vector3(levelWidthTile/2f, 0);
        if (spawn.x > GetLevelMaxX()) spawn -= new Vector3(levelWidthTile/2f, 0);
        return spawn;
    }
    [Range(1,10)]
    public int playersToVisualize = 10;
    void OnDrawGizmosSelected() {
        for (int i = 0; i < playersToVisualize; i++) {
            Gizmos.color = new Color(i/playersToVisualize, 0, 0, 0.75f);
            Gizmos.DrawCube(GetSpawnpoint(i, playersToVisualize) + Vector3.down/4f, Vector2.one/2f);
        }
    }
}
