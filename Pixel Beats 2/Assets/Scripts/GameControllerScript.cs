﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class GameControllerScript : MonoBehaviour
{
    public bool autoGenerateSequence = true;
    public Tilemap map;
    public string sequenceRaw;

    Vector3Int[] sequence;

    void Awake() {
        ProcessTilemap();
        if (autoGenerateSequence)
            GenerateRandomSequence();
        else
            ProcessSequence();        
    }

    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(Beats());
    }

    // Update is called once per frame
    void Update()
    {
        DetectPlayerTaps();
    }

    void DetectPlayerTaps() {
        if (Input.GetKeyDown(KeyCode.Mouse0)) {
            Vector3Int pos = RoundDownVector3(Camera.main.ScreenToWorldPoint(Input.mousePosition));
            //see if the user clicked on a valid note that is currently listening for input
            foreach (Note n in clickableNotes) {
                if(n.location == pos) {
                    OnNoteClicked(n);
                    clickableNotes.Remove(n);
                    break;
                }
            }
        }
    }

    enum NoteResult {
        Perfect, Great, Ok, Bad, Missed
    };

    public Text indicatorText;

    void OnNoteClicked(Note note) {
        float timeDifference = Mathf.Abs(Time.time - note.spawnTime - beatDuration - 1);

        if(timeDifference < 0.2f) {
            UpdateIndicatorAndStreak(NoteResult.Perfect);
        } else if(timeDifference < 0.4f){
            UpdateIndicatorAndStreak(NoteResult.Great);
        } else if (timeDifference < 0.6f) {
            UpdateIndicatorAndStreak(NoteResult.Ok);
        } else {
            UpdateIndicatorAndStreak(NoteResult.Bad);
        }

        note.frame.SetActive(false);
    }

    void OnNoteMissed() {
        UpdateIndicatorAndStreak(NoteResult.Missed);
    }


    int streak = 0;
    void UpdateIndicatorAndStreak(NoteResult result) {
        

        //kill streak
        if(result == NoteResult.Missed || result == NoteResult.Bad) {
            streak = 0;
        } else {
            streak++;
        }

        switch (result) {
            case NoteResult.Perfect:
                indicatorText.text = "Perfect! x" + streak;
                break;
            case NoteResult.Great:
                indicatorText.text = "Great! x" + streak;
                break;
            case NoteResult.Ok:
                indicatorText.text = "Ok! x" + streak;
                break;
            case NoteResult.Bad:
                indicatorText.text = "Bad!";
                break;
            case NoteResult.Missed:
                indicatorText.text = "Missed!";
                break;
        }
    }


    List<KeyValuePair<TileBase, short>> bases = new List<KeyValuePair<TileBase, short>>();
    short[,] bitmap;
    List<Vector3Int> tilePositions = new List<Vector3Int>();
    void ProcessTilemap() {
        bitmap = new short[map.cellBounds.size.x, map.cellBounds.size.y];
        for(int i = map.cellBounds.x; i < map.cellBounds.xMax; i++) {
            for(int j = map.cellBounds.y; j < map.cellBounds.yMax; j++) {
                Vector3Int pos = new Vector3Int(i, j, 0);
                TileBase tile = map.GetTile(pos);
                if(tile!= null) {
                    tilePositions.Add(pos);
                    //translate tilebase to an id (0,1,2...) based on dictionary (bases)
                    short id;
                    
                    KeyValuePair<TileBase, short> pair = GetPair(tile);
                    
                    //Add if doesn't exist
                    if (pair.Key == null) {
                        KeyValuePair<TileBase, short> newPair = new KeyValuePair<TileBase, short>(tile, (short)bases.Count);
                        bases.Add(newPair);
                        pair = newPair;
                    }

                    id = pair.Value;                   

                    bitmap[i-map.cellBounds.x, j-map.cellBounds.y] = id;

                    //hide the tile
                    SetTileActive(pos, false);
                } else {
                    bitmap[i-map.cellBounds.x, j-map.cellBounds.y] = -1;
                }
            }
        }
    }

    void GenerateRandomSequence() {
        //randomize the location list
        for (int i = 0; i < tilePositions.Count; i++) {
            Vector3Int temp = tilePositions[i];
            int randomIndex = Random.Range(i, tilePositions.Count);
            tilePositions[i] = tilePositions[randomIndex];
            tilePositions[randomIndex] = temp;
        }

        //copy tilePositions to sequence
        sequence = new Vector3Int[tilePositions.Count];
        for (int i = 0; i < tilePositions.Count; i++) {
            sequence[i] = tilePositions[i];
        }
    }


    //convert string ex: 1,2;6,3;7,1 into coordinates (1,2), (6,3), (7,1)
    void ProcessSequence() {
        string[] pairTokens = sequenceRaw.Split(';');
        sequence = new Vector3Int[pairTokens.Length];
        for(int i = 0; i < pairTokens.Length; i++) {
            string[] coordTokens = pairTokens[i].Split(',');
            sequence[i] = new Vector3Int(int.Parse(coordTokens[0]), int.Parse(coordTokens[1]), 0);
        }
    }
    
    


    void SetTileActive(Vector3Int pos, bool active) {
        int tileSpaceX = pos.x - map.cellBounds.x, tileSpaceY = pos.y - map.cellBounds.y;
        
        if(tileSpaceX >= 0 && tileSpaceY >= 0 && pos.x < map.cellBounds.xMax && pos.y < map.cellBounds.yMax) {
            short id = bitmap[tileSpaceX, tileSpaceY];
            TileBase tileToReveal = GetPair(id).Key;

            if (active) {
                if (tileToReveal != null && bitmap != null) {
                    map.SetTile(new Vector3Int(pos.x, pos.y, 0), tileToReveal);
                }
            } else {
                map.SetTile(new Vector3Int(pos.x, pos.y, 0), null);
            }
        }    
    }




    struct Note {
        public Vector3Int location;
        public float spawnTime;
        public GameObject frame;

        public Note(Vector3Int v, float t, GameObject frame) {
            location = v;
            spawnTime = t;
            this.frame = frame;
        }
    };

    List<Note> clickableNotes = new List<Note>();

    public float afterNoteThreshold; //how many seconds can the user still click after the note has already disappeared

    public int concurrentNotes = 3; //how many notes to show ahead of the current note
    public float noteTurnSpeed = 60, noteShrinkSpeed;
    public GameObject framePrefab;

    Queue<GameObject> framesQueue = new Queue<GameObject>(); //for reuse instead of destroying

    IEnumerator AnimateNote(Vector3Int location) {

        GameObject frame;
        Vector3 framePos = new Vector3(sequence[seqIndex].x + 0.5f, sequence[seqIndex].y + 0.5f, -1);

        //try to reuse from existing frames
        if(framesQueue.Count != 0 && !framesQueue.Peek().activeInHierarchy) {
            frame = framesQueue.Dequeue();
        } else {
            //make new frame
            frame = Instantiate(framePrefab, framePos, Quaternion.identity);
        }
        framesQueue.Enqueue(frame);

        //reset the frame
        frame.SetActive(true);
        frame.transform.position = framePos;        
        Transform outerFrameTransform = frame.transform.GetChild(0);
        outerFrameTransform.localScale = Vector3.one * 0.8f;

        Text noteText = frame.GetComponentInChildren<Text>();

        noteText.text = (seqIndex % 4 + 1).ToString(); // change text of note to 1, 2, 3, 4

        Vector3 targetAngles = transform.eulerAngles + 180f * Vector3.forward;
        SetTileActive(sequence[seqIndex], true);

        Note note = new Note(sequence[seqIndex], Time.time, frame);
        clickableNotes.Add(note);

        //smoothly animate the outer ring to spin to targetAngles (full 180)
        float timeElapsed = 0;
        while(timeElapsed < beatDuration * concurrentNotes) {
            // outerFrameTransform.eulerAngles = Vector3.Lerp(outerFrameTransform.eulerAngles, targetAngles, noteTurnSpeed * Time.deltaTime);
            // outerFrameTransform.localScale = Vector3.Lerp(outerFrameTransform.localScale, new Vector3(0.2f, 0.2f, 1f), noteShrinkSpeed * Time.deltaTime);

            //rotate slowly
            outerFrameTransform.eulerAngles += Vector3.forward * noteTurnSpeed * Time.deltaTime;
            //shrink slowly
            outerFrameTransform.localScale += (Vector3.left + Vector3.down) * noteShrinkSpeed * Time.deltaTime;
            
            
            timeElapsed += Time.deltaTime;
            yield return new WaitForEndOfFrame();           
        }
        outerFrameTransform.eulerAngles = targetAngles;
        frame.SetActive(false);

        yield return new WaitForSeconds(afterNoteThreshold);
        //if user did not click on this note
        if (clickableNotes.Contains(note)) {
            OnNoteMissed();
            clickableNotes.Remove(note);
        }       
    }


    public float beatDuration = 0.8f;
    int seqIndex = 0;

    //Called recursively every beat
    IEnumerator Beats() {

        StartCoroutine(AnimateNote(sequence[seqIndex]));
        seqIndex++;

        yield return new WaitForSeconds(beatDuration);

        if (seqIndex < sequence.Length)
            StartCoroutine(Beats());
    }



    KeyValuePair<TileBase, short> GetPair(short id) {
        for (int i = 0; i < bases.Count; i++) {
            if (bases[i].Value == id)
                return bases[i];
        }
        return new KeyValuePair<TileBase, short>(null, -2);
    }
    KeyValuePair<TileBase, short> GetPair(TileBase t) {
        for (int i = 0; i < bases.Count; i++) {
            if (bases[i].Key == t)
                return bases[i];
        }
        return new KeyValuePair<TileBase, short>(null, -2);
    }


    const float roundThreshold = 0.2f;
    Vector3Int RoundDownVector3(Vector3 v) {        
        return new Vector3Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), 0);
    }
    Vector3Int RoundClosestVector3(Vector3 v) {
        return new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), 0);
    }

}